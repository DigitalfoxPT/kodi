/*
 *  Copyright (C) 2023 Team Kodi
 *  This file is part of Kodi - https://kodi.tv
 *
 *  SPDX-License-Identifier: GPL-2.0-or-later
 *  See LICENSES/README.md for more information.
 */

#include "VideoGeneratedImageFileLoader.h"

#include "DVDFileInfo.h"
#include "FileItem.h"
#include "SeekHandler.h"
#include "ServiceBroker.h"
#include "URL.h"
#include "application/ApplicationComponents.h"
#include "application/ApplicationPlayer.h"
#include "filesystem/DirectoryCache.h"
#include "guilib/Texture.h"
#include "settings/Settings.h"
#include "settings/SettingsComponent.h"
#include "utils/StringUtils.h"
#include "utils/URIUtils.h"
#include "utils/log.h"
#include "video/VideoInfoTag.h"

#include <algorithm>
#include <mutex>

bool VIDEO::CVideoGeneratedImageFileLoader::CanLoad(const std::string& specialType) const
{
  return specialType == "video" || StringUtils::StartsWith(specialType, "videoseek_");
}

namespace
{
std::mutex g_seekPreviewExtractionMutex;

void SetupRarOptions(CFileItem& item, const std::string& path)
{
  std::string path2(path);
  if (item.IsVideoDb() && item.HasVideoInfoTag())
    path2 = item.GetVideoInfoTag()->m_strFileNameAndPath;
  CURL url(path2);
  std::string opts = url.GetOptions();
  if (opts.find("flags") != std::string::npos)
    return;
  if (opts.size())
    opts += "&flags=8";
  else
    opts = "?flags=8";
  url.SetOptions(opts);
  if (item.IsVideoDb() && item.HasVideoInfoTag())
    item.GetVideoInfoTag()->m_strFileNameAndPath = url.Get();
  else
    item.SetPath(url.Get());
  g_directoryCache.ClearDirectory(url.GetWithoutFilename());
}
} // namespace

std::unique_ptr<CTexture> VIDEO::CVideoGeneratedImageFileLoader::Load(
    const std::string& specialType, const std::string& filePath, unsigned int, unsigned int) const
{
  const bool seekPreview = StringUtils::StartsWith(specialType, "videoseek_");
  if (!seekPreview && !CServiceBroker::GetSettingsComponent()->GetSettings()->GetBool(
                          CSettings::SETTING_MYVIDEOS_EXTRACTTHUMB))
  {
    return {};
  }

  CFileItem item{filePath, false};

  if (URIUtils::IsInRAR(filePath))
    SetupRarOptions(item, filePath);

  int64_t seekTimeMs = -1;
  if (seekPreview)
  {
    try
    {
      seekTimeMs = std::stoll(specialType.substr(10)) * 1000;
    }
    catch (...)
    {
      return {};
    }
  }

  if (seekPreview)
  {
    // Android TV can request several different preview textures while the user
    // presses left/right rapidly. Serialise decoder access and discard requests
    // that became obsolete while waiting. This prevents multiple FFmpeg
    // instances from competing with MediaCodec and keeps the newest target at
    // the front of the useful work.
    std::unique_lock<std::mutex> previewLock(g_seekPreviewExtractionMutex);

    const auto& components = CServiceBroker::GetAppComponents();
    const auto appPlayer = components.GetComponent<CApplicationPlayer>();
    const CSeekHandler& seekHandler = appPlayer->GetSeekHandler();
    constexpr int64_t PREVIEW_INTERVAL_MS = 10000;
    const int64_t totalTimeMs = appPlayer->GetTotalTime();
    int64_t currentTargetMs = static_cast<int64_t>(seekHandler.GetSeekPreviewTime()) * 1000;
    currentTargetMs =
        std::clamp(currentTargetMs, int64_t{0}, std::max(int64_t{0}, totalTimeMs - 1));
    currentTargetMs = (currentTargetMs / PREVIEW_INTERVAL_MS) * PREVIEW_INTERVAL_MS;
    if (!appPlayer->IsPlayingVideo() || !seekHandler.IsSeekPreviewActive() ||
        currentTargetMs != seekTimeMs)
    {
      CLog::Log(LOGDEBUG, "Seek preview: skipping obsolete frame at {}ms", seekTimeMs);
      return {};
    }

    CLog::Log(LOGINFO, "Seek preview: extracting frame at {}ms from {}", seekTimeMs,
              CURL::GetRedacted(filePath));

    std::unique_ptr<CTexture> texture = CDVDFileInfo::ExtractThumbToTexture(item, 0, seekTimeMs);
    if (texture)
      CLog::Log(LOGINFO, "Seek preview: frame extracted successfully at {}ms", seekTimeMs);
    else
      CLog::Log(LOGWARNING, "Seek preview: failed to extract frame at {}ms from {}", seekTimeMs,
                CURL::GetRedacted(filePath));
    return texture;
  }

  return CDVDFileInfo::ExtractThumbToTexture(item, 0, seekTimeMs);
}
