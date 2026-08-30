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
#include "ServiceBroker.h"
#include "URL.h"
#include "filesystem/DirectoryCache.h"
#include "filesystem/File.h"
#include "guilib/Texture.h"
#include "settings/Settings.h"
#include "settings/SettingsComponent.h"
#include "utils/StringUtils.h"
#include "utils/URIUtils.h"
#include "utils/log.h"
#include "video/VideoInfoTag.h"

#include <algorithm>
#include <array>
#include <cstdint>
#include <limits>
#include <vector>

bool VIDEO::CVideoGeneratedImageFileLoader::CanLoad(const std::string& specialType) const
{
  return specialType == "video" || StringUtils::StartsWith(specialType, "bifseek_");
}

namespace
{
constexpr std::array<uint8_t, 8> BIF_MAGIC{0x89, 0x42, 0x49, 0x46,
                                           0x0d, 0x0a, 0x1a, 0x0a};
constexpr size_t BIF_HEADER_SIZE = 64;
constexpr uint32_t BIF_DEFAULT_TIMESTAMP_MULTIPLIER_MS = 1000;
constexpr uint32_t BIF_SENTINEL_TIMESTAMP = std::numeric_limits<uint32_t>::max();
constexpr uint32_t BIF_MAX_IMAGES = 1'000'000;
constexpr uint64_t BIF_MAX_JPEG_SIZE = 32ULL * 1024ULL * 1024ULL;

uint32_t ReadLittleEndian32(const uint8_t* bytes)
{
  return static_cast<uint32_t>(bytes[0]) |
         (static_cast<uint32_t>(bytes[1]) << 8) |
         (static_cast<uint32_t>(bytes[2]) << 16) |
         (static_cast<uint32_t>(bytes[3]) << 24);
}

bool ReadExactly(XFILE::CFile& file, void* destination, size_t size)
{
  auto* bytes = static_cast<uint8_t*>(destination);
  size_t totalRead = 0;
  while (totalRead < size)
  {
    const ssize_t read = file.Read(bytes + totalRead, size - totalRead);
    if (read <= 0)
      return false;
    totalRead += static_cast<size_t>(read);
  }
  return true;
}

std::unique_ptr<CTexture> LoadBifFrame(const std::string& bifPath,
                                       int64_t targetTimeMs,
                                       unsigned int preferredWidth,
                                       unsigned int preferredHeight)
{
  XFILE::CFile file;
  if (!file.Open(bifPath))
  {
    CLog::Log(LOGWARNING, "Seek preview: unable to open BIF sidecar {}",
              CURL::GetRedacted(bifPath));
    return {};
  }

  const int64_t fileLength = file.GetLength();
  if (fileLength < static_cast<int64_t>(BIF_HEADER_SIZE))
    return {};

  std::array<uint8_t, BIF_HEADER_SIZE> header{};
  if (!ReadExactly(file, header.data(), header.size()) ||
      !std::equal(BIF_MAGIC.begin(), BIF_MAGIC.end(), header.begin()))
  {
    CLog::Log(LOGWARNING, "Seek preview: invalid BIF header in {}",
              CURL::GetRedacted(bifPath));
    return {};
  }

  const uint32_t version = ReadLittleEndian32(header.data() + 8);
  const uint32_t imageCount = ReadLittleEndian32(header.data() + 12);
  uint32_t timestampMultiplierMs = ReadLittleEndian32(header.data() + 16);
  if (version != 0 || imageCount == 0 || imageCount > BIF_MAX_IMAGES)
    return {};
  if (timestampMultiplierMs == 0)
    timestampMultiplierMs = BIF_DEFAULT_TIMESTAMP_MULTIPLIER_MS;

  const uint64_t indexSize = static_cast<uint64_t>(imageCount + 1) * 8;
  if (indexSize > static_cast<uint64_t>(fileLength) - BIF_HEADER_SIZE)
    return {};

  std::vector<uint8_t> index(static_cast<size_t>(indexSize));
  if (!ReadExactly(file, index.data(), index.size()))
    return {};

  const uint64_t clampedTargetMs = static_cast<uint64_t>(std::max<int64_t>(0, targetTimeMs));
  uint32_t selectedImage = 0;
  for (uint32_t image = 0; image < imageCount; ++image)
  {
    const uint32_t timestamp = ReadLittleEndian32(index.data() + image * 8);
    const uint64_t timestampMs = static_cast<uint64_t>(timestamp) * timestampMultiplierMs;
    if (timestampMs > clampedTargetMs)
      break;
    selectedImage = image;
  }

  const size_t selectedEntry = static_cast<size_t>(selectedImage) * 8;
  const size_t nextEntry = static_cast<size_t>(selectedImage + 1) * 8;
  const uint32_t imageOffset = ReadLittleEndian32(index.data() + selectedEntry + 4);
  const uint32_t nextTimestamp = ReadLittleEndian32(index.data() + nextEntry);
  const uint32_t nextOffset = ReadLittleEndian32(index.data() + nextEntry + 4);
  if (selectedImage + 1 == imageCount && nextTimestamp != BIF_SENTINEL_TIMESTAMP)
    return {};
  if (imageOffset < BIF_HEADER_SIZE + indexSize || nextOffset <= imageOffset ||
      nextOffset > static_cast<uint64_t>(fileLength))
  {
    return {};
  }

  const uint64_t jpegSize = static_cast<uint64_t>(nextOffset) - imageOffset;
  if (jpegSize < 4 || jpegSize > BIF_MAX_JPEG_SIZE ||
      file.Seek(imageOffset, SEEK_SET) != imageOffset)
  {
    return {};
  }

  std::vector<uint8_t> jpeg(static_cast<size_t>(jpegSize));
  if (!ReadExactly(file, jpeg.data(), jpeg.size()) || jpeg[0] != 0xff || jpeg[1] != 0xd8)
    return {};

  CLog::Log(LOGDEBUG, "Seek preview: loaded BIF image {} at {}ms from {}", selectedImage,
            targetTimeMs, CURL::GetRedacted(bifPath));
  return CTexture::LoadFromFileInMemory(jpeg.data(), jpeg.size(), "image/jpeg",
                                        preferredWidth, preferredHeight);
}

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
    const std::string& specialType,
    const std::string& filePath,
    unsigned int preferredWidth,
    unsigned int preferredHeight) const
{
  const bool seekPreview = StringUtils::StartsWith(specialType, "bifseek_");
  if (!seekPreview && !CServiceBroker::GetSettingsComponent()->GetSettings()->GetBool(
                          CSettings::SETTING_MYVIDEOS_EXTRACTTHUMB))
  {
    return {};
  }

  CFileItem item{filePath, false};

  if (!seekPreview && URIUtils::IsInRAR(filePath))
    SetupRarOptions(item, filePath);

  int64_t seekTimeMs = -1;
  if (seekPreview)
  {
    try
    {
      seekTimeMs = std::stoll(specialType.substr(8));
    }
    catch (...)
    {
      return {};
    }
  }

  if (seekPreview)
    return LoadBifFrame(filePath, seekTimeMs, preferredWidth, preferredHeight);

  return CDVDFileInfo::ExtractThumbToTexture(item, 0, seekTimeMs);
}
