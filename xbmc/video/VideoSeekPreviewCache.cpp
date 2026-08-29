/*
 *  Copyright (C) 2005-2026 Team Kodi
 *  This file is part of Kodi - https://kodi.tv
 *
 *  SPDX-License-Identifier: GPL-2.0-or-later
 *  See LICENSES/README.md for more information.
 */

#include "VideoSeekPreviewCache.h"

#if defined(TARGET_ANDROID)

#include "FileItem.h"
#include "ServiceBroker.h"
#include "TextureCache.h"
#include "TextureDatabase.h"
#include "URL.h"
#include "application/ApplicationComponents.h"
#include "application/ApplicationPlayer.h"
#include "cores/VideoPlayer/DVDFileInfo.h"
#include "utils/StringUtils.h"
#include "utils/Variant.h"
#include "utils/log.h"
#include "video/VideoDatabase.h"
#include "video/VideoInfoTag.h"

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <map>
#include <memory>
#include <string>
#include <vector>

namespace
{
constexpr int64_t SEEK_PREVIEW_INTERVAL_MS = 10'000;
constexpr auto SEEK_PREVIEW_CACHE_BUDGET = std::chrono::minutes{5};

struct SeekPreviewVideo
{
  std::string path;
  int64_t durationMs{0};
  CDateTime dateAdded;
};

void AddLibraryItems(const CFileItemList& items,
                     std::map<std::string, SeekPreviewVideo>& videos)
{
  for (const auto& item : items)
  {
    if (!item || !item->HasVideoInfoTag())
      continue;

    const CVideoInfoTag* tag = item->GetVideoInfoTag();
    std::string path = item->GetDynPath();
    if (path.empty())
      path = tag->m_strFileNameAndPath;
    if (path.empty())
      continue;

    int64_t durationMs = static_cast<int64_t>(tag->GetDuration()) * 1000;
    if (durationMs <= 0)
      durationMs = static_cast<int64_t>(tag->GetResumePoint().totalTimeInSeconds * 1000.0);

    auto [it, inserted] =
        videos.try_emplace(path, SeekPreviewVideo{path, durationMs, tag->m_dateAdded});
    if (!inserted)
    {
      it->second.durationMs = std::max(it->second.durationMs, durationMs);
      if (it->second.dateAdded < tag->m_dateAdded)
        it->second.dateAdded = tag->m_dateAdded;
    }
  }
}

std::vector<SeekPreviewVideo> GetLibraryVideos(CVideoDatabase& database)
{
  std::map<std::string, SeekPreviewVideo> videosByPath;

  CFileItemList movies;
  database.GetMoviesByWhere("videodb://movies/titles/", CDatabase::Filter(), movies);
  AddLibraryItems(movies, videosByPath);

  CFileItemList episodes;
  database.GetEpisodesByWhere("videodb://tvshows/titles/", CDatabase::Filter(), episodes,
                              false);
  AddLibraryItems(episodes, videosByPath);

  CFileItemList musicVideos;
  database.GetMusicVideosByWhere("videodb://musicvideos/titles/", CDatabase::Filter(),
                                 musicVideos, true);
  AddLibraryItems(musicVideos, videosByPath);

  std::vector<SeekPreviewVideo> videos;
  videos.reserve(videosByPath.size());
  for (auto& video : videosByPath)
    videos.emplace_back(std::move(video.second));

  // Generate newly-added content first. Existing cached images are skipped,
  // so following library scans naturally resume any unfinished video.
  std::sort(videos.begin(), videos.end(),
            [](const SeekPreviewVideo& left, const SeekPreviewVideo& right)
            { return left.dateAdded > right.dateAdded; });
  return videos;
}

std::string GetPreviewImageUrl(const std::string& path, int64_t targetTimeMs)
{
  return CTextureUtils::GetWrappedImageURL(
      path, StringUtils::Format("videoseek_{}", targetTimeMs));
}

unsigned int RemoveObsoleteImages(const std::vector<SeekPreviewVideo>& videos)
{
  std::map<std::string, int64_t> validVideos;
  for (const SeekPreviewVideo& video : videos)
    validVideos.emplace(video.path, video.durationMs);

  CTextureDatabase textureDatabase;
  if (!textureDatabase.Open())
    return 0;

  CVariant textures(CVariant::VariantTypeArray);
  if (!textureDatabase.GetTextures(textures, CDatabase::Filter()))
    return 0;

  std::vector<int> obsoleteIds;
  for (auto texture = textures.begin_array(); texture != textures.end_array(); ++texture)
  {
    const CURL imageUrl((*texture)["url"].asString());
    const std::string specialType = imageUrl.GetUserName();
    if (!StringUtils::StartsWith(specialType, "videoseek_"))
      continue;

    int64_t targetTimeMs = -1;
    try
    {
      targetTimeMs = std::stoll(specialType.substr(10));
    }
    catch (...)
    {
    }

    const auto video = validVideos.find(imageUrl.GetHostName());
    const bool invalidTarget =
        targetTimeMs < 0 || targetTimeMs % SEEK_PREVIEW_INTERVAL_MS != 0;
    const bool pastKnownDuration = video != validVideos.end() && video->second > 0 &&
                                   targetTimeMs >= video->second;
    if (video == validVideos.end() || invalidTarget || pastKnownDuration)
      obsoleteIds.emplace_back(static_cast<int>((*texture)["textureid"].asInteger()));
  }

  for (const int textureId : obsoleteIds)
    CServiceBroker::GetTextureCache()->ClearCachedImage(textureId);

  return static_cast<unsigned int>(obsoleteIds.size());
}

void GenerateMissingImages(const std::vector<SeekPreviewVideo>& videos)
{
  const auto appPlayer =
      CServiceBroker::GetAppComponents().GetComponent<CApplicationPlayer>();
  const std::shared_ptr<CTextureCache> textureCache = CServiceBroker::GetTextureCache();
  if (!textureCache)
    return;

  unsigned int generated = 0;
  unsigned int alreadyCached = 0;
  unsigned int failed = 0;
  const auto started = std::chrono::steady_clock::now();

  CLog::Log(LOGINFO,
            "Seek preview cache: checking {} library videos at ten-second intervals",
            videos.size());

  for (const SeekPreviewVideo& video : videos)
  {
    if (video.durationMs <= 0)
    {
      CLog::Log(LOGDEBUG, "Seek preview cache: no duration for {}, skipping",
                CURL::GetRedacted(video.path));
      continue;
    }

    if (!CDVDFileInfo::CanExtract(CFileItem{video.path, false}))
    {
      CLog::Log(LOGDEBUG, "Seek preview cache: unsupported source {}, skipping",
                CURL::GetRedacted(video.path));
      continue;
    }

    for (int64_t targetTimeMs = 0; targetTimeMs < video.durationMs;
         targetTimeMs += SEEK_PREVIEW_INTERVAL_MS)
    {
      if (std::chrono::steady_clock::now() - started >= SEEK_PREVIEW_CACHE_BUDGET)
      {
        CLog::Log(LOGINFO,
                  "Seek preview cache: five-minute update budget reached ({} generated, {} "
                  "already cached, {} failed); the next library update will resume",
                  generated, alreadyCached, failed);
        return;
      }

      // Playback always has priority. A later library update resumes at the
      // first missing image because completed frames live in TextureCache.
      if (appPlayer->IsPlayingVideo())
      {
        CLog::Log(LOGINFO,
                  "Seek preview cache: stopped for video playback ({} generated, {} already "
                  "cached, {} failed)",
                  generated, alreadyCached, failed);
        return;
      }

      const std::string imageUrl = GetPreviewImageUrl(video.path, targetTimeMs);
      if (textureCache->HasCachedImage(imageUrl))
      {
        ++alreadyCached;
        continue;
      }

      CTextureDetails details;
      if (textureCache->CacheImage(imageUrl, details))
        ++generated;
      else
        ++failed;
    }
  }

  CLog::Log(LOGINFO,
            "Seek preview cache: complete ({} generated, {} already cached, {} failed)",
            generated, alreadyCached, failed);
}
} // unnamed namespace

#endif // TARGET_ANDROID

void VIDEO::CVideoSeekPreviewCache::Update(CVideoDatabase& database, bool generateMissing)
{
#if defined(TARGET_ANDROID)
  const std::vector<SeekPreviewVideo> videos = GetLibraryVideos(database);
  const unsigned int removed = RemoveObsoleteImages(videos);
  if (removed > 0)
    CLog::Log(LOGINFO, "Seek preview cache: removed {} obsolete images", removed);

  if (generateMissing)
    GenerateMissingImages(videos);
#else
  (void)database;
  (void)generateMissing;
#endif
}
