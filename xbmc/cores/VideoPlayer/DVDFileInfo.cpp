/*
 *  Copyright (C) 2005-2018 Team Kodi
 *  This file is part of Kodi - https://kodi.tv
 *
 *  SPDX-License-Identifier: GPL-2.0-or-later
 *  See LICENSES/README.md for more information.
 */

#include "DVDFileInfo.h"

#include "DVDInputStreams/DVDInputStream.h"
#include "DVDStreamInfo.h"
#include "FileItem.h"
#include "ServiceBroker.h"
#include "filesystem/StackDirectory.h"
#include "guilib/Texture.h"
#include "pictures/Picture.h"
#include "settings/AdvancedSettings.h"
#include "settings/SettingsComponent.h"
#include "utils/MemUtils.h"
#include "utils/URIUtils.h"
#include "utils/log.h"
#include "video/VideoInfoTag.h"
#ifdef HAVE_LIBBLURAY
#include "DVDInputStreams/DVDInputStreamBluray.h"
#endif
#include "DVDInputStreams/DVDFactoryInputStream.h"
#include "Interface/TimingConstants.h"
#include "DVDDemuxers/DVDDemux.h"
#include "DVDDemuxers/DVDDemuxUtils.h"
#include "DVDDemuxers/DVDFactoryDemuxer.h"
#include "DVDCodecs/DVDFactoryCodec.h"
#include "DVDCodecs/Video/DVDVideoCodec.h"
#include "DVDCodecs/Video/DVDVideoCodecFFmpeg.h"
#include "DVDDemuxers/DVDDemuxVobsub.h"
#include "Process/ProcessInfo.h"

#include "filesystem/File.h"
#include "cores/FFmpeg.h"
#include "TextureCache.h"
#include "Util.h"
#include "utils/LangCodeExpander.h"

#include <algorithm>
#include <chrono>
#include <cstdlib>
#include <memory>
#include <mutex>

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libswscale/swscale.h>
}

bool CDVDFileInfo::GetFileDuration(const std::string &path, int& duration)
{
  std::unique_ptr<CDVDDemux> demux;

  CFileItem item(path, false);
  auto input = CDVDFactoryInputStream::CreateInputStream(NULL, item);
  if (!input)
    return false;

  if (!input->Open())
    return false;

  demux.reset(CDVDFactoryDemuxer::CreateDemuxer(input, true));
  if (!demux)
    return false;

  duration = demux->GetStreamLength();
  if (duration > 0)
    return true;
  else
    return false;
}

int DegreeToOrientation(int degrees)
{
  switch(degrees)
  {
    case 90:
      return 5;
    case 180:
      return 2;
    case 270:
      return 7;
    default:
      return 0;
  }
}

namespace
{
constexpr unsigned int SEEK_PREVIEW_MAX_WIDTH = 480;

std::unique_ptr<CTexture> CreateTextureFromPicture(const VideoPicture& picture,
                                                   const CDVDStreamInfo& hint,
                                                   unsigned int preferredWidth,
                                                   unsigned int preferredHeight)
{
  if (!picture.videoBuffer || picture.iWidth == 0 || picture.iHeight == 0 ||
      picture.iDisplayWidth == 0 || picture.iDisplayHeight == 0)
  {
    return {};
  }

  unsigned int width = std::min(picture.iDisplayWidth, preferredWidth);
  double aspect = static_cast<double>(picture.iDisplayWidth) /
                  static_cast<double>(picture.iDisplayHeight);
  if (hint.forced_aspect && hint.aspect > 0.0)
    aspect = hint.aspect;

  unsigned int height = static_cast<unsigned int>(static_cast<double>(width) / aspect);
  if (preferredHeight > 0 && height > preferredHeight)
  {
    height = preferredHeight;
    width = static_cast<unsigned int>(static_cast<double>(height) * aspect);
  }

  width = std::max(1U, width);
  height = std::max(1U, height);

  auto result = CTexture::CreateTexture(width, height);
  if (!result)
    return {};
  result->SetAlpha(false);

  SwsContext* context =
      sws_getContext(picture.iWidth, picture.iHeight, AV_PIX_FMT_YUV420P, width, height,
                     AV_PIX_FMT_BGRA, SWS_FAST_BILINEAR, nullptr, nullptr, nullptr);
  if (!context)
    return {};

  uint8_t* planes[YuvImage::MAX_PLANES];
  int strides[YuvImage::MAX_PLANES];
  picture.videoBuffer->GetPlanes(planes);
  picture.videoBuffer->GetStrides(strides);
  uint8_t* src[4] = {planes[0], planes[1], planes[2], nullptr};
  int srcStrides[4] = {strides[0], strides[1], strides[2], 0};
  uint8_t* dst[4] = {result->GetPixels(), nullptr, nullptr, nullptr};
  int dstStrides[4] = {static_cast<int>(result->GetPitch()), 0, 0, 0};

  result->SetOrientation(DegreeToOrientation(hint.orientation));
  sws_scale(context, src, srcStrides, 0, picture.iHeight, dst, dstStrides);
  sws_freeContext(context);
  return result;
}

class CSeekPreviewSession
{
public:
  std::unique_ptr<CTexture> Extract(const CFileItem& fileItem,
                                    int64_t seekTimeMs,
                                    unsigned int preferredWidth,
                                    unsigned int preferredHeight)
  {
    if (!Open(fileItem))
      return {};

    const int64_t targetMs =
        std::clamp(seekTimeMs, int64_t{0}, std::max(int64_t{0}, m_totalLengthMs - 1));
    const auto start = std::chrono::steady_clock::now();

    // Seek backwards to a keyframe, exactly as a dedicated preview player would.
    // Resetting only the codec keeps the SMB input and demuxer alive between requests.
    m_videoCodec->Reset();
    m_videoCodec->SetCodecControl(DVD_CODEC_CTRL_NO_POSTPROC);
    if (!m_demuxer->SeekTime(static_cast<double>(targetMs), true))
    {
      CLog::Log(LOGWARNING, "Seek preview session: seek to {}ms failed in {}", targetMs,
                CURL::GetRedacted(m_path));
      return {};
    }

    CDVDVideoCodec::VCReturn decoderState = CDVDVideoCodec::VC_NONE;
    VideoPicture picture{};
    int packetsTried = 0;
    int packetsRemaining = std::max(160, m_demuxer->GetNrOfStreams() * 160);

    while (packetsRemaining-- > 0)
    {
      DemuxPacket* packet = m_demuxer->Read();
      ++packetsTried;
      if (!packet)
        break;

      if (packet->iStreamId != m_videoStream || packet->demuxerId != m_demuxerId)
      {
        CDVDDemuxUtils::FreeDemuxPacket(packet);
        continue;
      }

      m_videoCodec->AddData(*packet);
      CDVDDemuxUtils::FreeDemuxPacket(packet);

      decoderState = CDVDVideoCodec::VC_NONE;
      while (decoderState == CDVDVideoCodec::VC_NONE)
        decoderState = m_videoCodec->GetPicture(&picture);

      if (decoderState == CDVDVideoCodec::VC_PICTURE &&
          !(picture.iFlags & DVP_FLAG_DROPPED))
      {
        const double picturePts =
            picture.pts != DVD_NOPTS_VALUE ? picture.pts : picture.dts;
        const int64_t pictureTimeMs = picturePts != DVD_NOPTS_VALUE
                                          ? DVD_TIME_TO_MSEC(picturePts)
                                          : targetMs;

        // The demuxer seeks to the preceding keyframe. Decode through that
        // keyframe interval so the preview is the first frame at or after the
        // same absolute target that OK will send to the main player.
        if (pictureTimeMs < targetMs)
        {
          picture.Reset();
          continue;
        }

        const unsigned int maxWidth =
            preferredWidth > 0 ? std::min(preferredWidth, SEEK_PREVIEW_MAX_WIDTH)
                               : SEEK_PREVIEW_MAX_WIDTH;
        auto texture =
            CreateTextureFromPicture(picture, m_hint, maxWidth, preferredHeight);
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - start);
        CLog::Log(
            LOGINFO,
            "Seek preview session: decoded frame {}ms for target {}ms in {}ms after {} packets "
            "(decoder reused)",
            pictureTimeMs, targetMs, elapsed.count(), packetsTried);
        return texture;
      }

      if (decoderState == CDVDVideoCodec::VC_ERROR ||
          decoderState == CDVDVideoCodec::VC_FATAL)
      {
        break;
      }
    }

    CLog::Log(LOGWARNING, "Seek preview session: decode failed at {}ms in {} after {} packets",
              targetMs, CURL::GetRedacted(m_path), packetsTried);
    return {};
  }

private:
  bool Open(const CFileItem& fileItem)
  {
    if (m_path == fileItem.GetPath() && m_inputStream && m_demuxer && m_videoCodec)
      return true;

    Reset();
    if (!CDVDFileInfo::CanExtract(fileItem))
      return false;

    CFileItem item(fileItem);
    item.SetMimeTypeForInternetFile();
    m_inputStream = CDVDFactoryInputStream::CreateInputStream(nullptr, item);
    if (!m_inputStream || !m_inputStream->Open())
    {
      CLog::Log(LOGERROR, "Seek preview session: failed to open {}",
                CURL::GetRedacted(fileItem.GetPath()));
      Reset();
      return false;
    }

    m_demuxer.reset(CDVDFactoryDemuxer::CreateDemuxer(m_inputStream, true));
    if (!m_demuxer)
    {
      CLog::Log(LOGERROR, "Seek preview session: failed to create demuxer for {}",
                CURL::GetRedacted(fileItem.GetPath()));
      Reset();
      return false;
    }

    for (CDemuxStream* stream : m_demuxer->GetStreams())
    {
      if (!stream)
        continue;

      if (m_videoStream < 0 && stream->type == STREAM_VIDEO &&
          !(stream->flags & AV_DISPOSITION_ATTACHED_PIC))
      {
        m_videoStream = stream->uniqueId;
        m_demuxerId = stream->demuxerId;
      }
      else
      {
        m_demuxer->EnableStream(stream->demuxerId, stream->uniqueId, false);
      }
    }

    CDemuxStream* stream = m_demuxer->GetStream(m_demuxerId, m_videoStream);
    if (!stream)
    {
      CLog::Log(LOGWARNING, "Seek preview session: no video stream in {}",
                CURL::GetRedacted(fileItem.GetPath()));
      Reset();
      return false;
    }

    m_processInfo.reset(CProcessInfo::CreateInstance());
    std::vector<AVPixelFormat> pixelFormats{AV_PIX_FMT_YUV420P};
    m_processInfo->SetPixFormats(pixelFormats);

    m_hint = CDVDStreamInfo(*stream, true);
    m_hint.codecOptions = CODEC_FORCE_SOFTWARE;
    m_videoCodec = CDVDFactoryCodec::CreateVideoCodec(m_hint, *m_processInfo);
    if (!m_videoCodec)
    {
      CLog::Log(LOGERROR, "Seek preview session: failed to create software decoder for {}",
                CURL::GetRedacted(fileItem.GetPath()));
      Reset();
      return false;
    }

    m_path = fileItem.GetPath();
    m_totalLengthMs = m_demuxer->GetStreamLength();
    CLog::Log(LOGINFO, "Seek preview session: opened persistent software decoder for {}",
              CURL::GetRedacted(m_path));
    return true;
  }

  void Reset()
  {
    m_videoCodec.reset();
    m_processInfo.reset();
    m_demuxer.reset();
    m_inputStream.reset();
    m_hint.Clear();
    m_path.clear();
    m_videoStream = -1;
    m_demuxerId = -1;
    m_totalLengthMs = 0;
  }

  std::string m_path;
  std::shared_ptr<CDVDInputStream> m_inputStream;
  std::unique_ptr<CDVDDemux> m_demuxer;
  std::unique_ptr<CProcessInfo> m_processInfo;
  std::unique_ptr<CDVDVideoCodec> m_videoCodec;
  CDVDStreamInfo m_hint;
  int m_videoStream{-1};
  int64_t m_demuxerId{-1};
  int64_t m_totalLengthMs{0};
};

std::mutex g_seekPreviewSessionMutex;
std::unique_ptr<CSeekPreviewSession> g_seekPreviewSession;
} // namespace

std::unique_ptr<CTexture> CDVDFileInfo::ExtractSeekPreviewToTexture(
    const CFileItem& fileItem,
    int64_t seekTimeMs,
    unsigned int preferredWidth,
    unsigned int preferredHeight)
{
  std::unique_lock<std::mutex> lock(g_seekPreviewSessionMutex);
  if (!g_seekPreviewSession)
    g_seekPreviewSession = std::make_unique<CSeekPreviewSession>();

  return g_seekPreviewSession->Extract(fileItem, seekTimeMs, preferredWidth, preferredHeight);
}

void CDVDFileInfo::ResetSeekPreview()
{
  std::unique_lock<std::mutex> lock(g_seekPreviewSessionMutex);
  g_seekPreviewSession.reset();
}

std::unique_ptr<CTexture> CDVDFileInfo::ExtractThumbToTexture(const CFileItem& fileItem,
                                                              int chapterNumber,
                                                              int64_t seekTimeMs)
{
  if (!CanExtract(fileItem))
    return {};

  const std::string redactPath = CURL::GetRedacted(fileItem.GetPath());
  auto start = std::chrono::steady_clock::now();

  CFileItem item(fileItem);
  item.SetMimeTypeForInternetFile();
  auto pInputStream = CDVDFactoryInputStream::CreateInputStream(NULL, item);
  if (!pInputStream)
  {
    CLog::Log(LOGERROR, "InputStream: Error creating stream for {}", redactPath);
    return {};
  }

  if (!pInputStream->Open())
  {
    CLog::Log(LOGERROR, "InputStream: Error opening, {}", redactPath);
    return {};
  }

  std::unique_ptr<CDVDDemux> demuxer{CDVDFactoryDemuxer::CreateDemuxer(pInputStream, true)};
  if (!demuxer)
  {
    CLog::LogF(LOGERROR, "Error creating demuxer");
    return {};
  }

  int nVideoStream = -1;
  int64_t demuxerId = -1;
  for (CDemuxStream* pStream : demuxer->GetStreams())
  {
    if (pStream)
    {
      // ignore if it's a picture attachment (e.g. jpeg artwork)
      if (pStream->type == STREAM_VIDEO && !(pStream->flags & AV_DISPOSITION_ATTACHED_PIC))
      {
        nVideoStream = pStream->uniqueId;
        demuxerId = pStream->demuxerId;
      }
      else
        demuxer->EnableStream(pStream->demuxerId, pStream->uniqueId, false);
    }
  }

  int packetsTried = 0;

  std::unique_ptr<CTexture> result{};
  if (nVideoStream != -1)
  {
    std::unique_ptr<CProcessInfo> pProcessInfo(CProcessInfo::CreateInstance());
    std::vector<AVPixelFormat> pixFmts;
    pixFmts.push_back(AV_PIX_FMT_YUV420P);
    pProcessInfo->SetPixFormats(pixFmts);

    CDVDStreamInfo hint(*demuxer->GetStream(demuxerId, nVideoStream), true);
    hint.codecOptions = CODEC_FORCE_SOFTWARE;

    std::unique_ptr<CDVDVideoCodec> pVideoCodec =
        CDVDFactoryCodec::CreateVideoCodec(hint, *pProcessInfo);

    if (pVideoCodec)
    {
      int nTotalLen = demuxer->GetStreamLength();

      bool seekToChapter = chapterNumber > 0 && demuxer->GetChapterCount() > 0;
      int64_t nSeekTo = seekTimeMs >= 0
                            ? std::min(seekTimeMs, static_cast<int64_t>(std::max(0, nTotalLen - 1)))
                            : (seekToChapter ? demuxer->GetChapterPos(chapterNumber) * 1000
                                             : nTotalLen / 3);

      CLog::LogF(LOGDEBUG, "seeking to pos {}ms (total: {}ms) in {}", nSeekTo, nTotalLen,
                 redactPath);

      if (demuxer->SeekTime(static_cast<double>(nSeekTo), true))
      {
        CDVDVideoCodec::VCReturn iDecoderState = CDVDVideoCodec::VC_NONE;
        VideoPicture picture = {};

        // num streams * 160 frames, should get a valid frame, if not abort.
        int abort_index = demuxer->GetNrOfStreams() * 160;
        do
        {
          DemuxPacket* pPacket = demuxer->Read();
          packetsTried++;

          if (!pPacket)
            break;

          if (pPacket->iStreamId != nVideoStream)
          {
            CDVDDemuxUtils::FreeDemuxPacket(pPacket);
            continue;
          }

          pVideoCodec->AddData(*pPacket);
          CDVDDemuxUtils::FreeDemuxPacket(pPacket);

          iDecoderState = CDVDVideoCodec::VC_NONE;
          while (iDecoderState == CDVDVideoCodec::VC_NONE)
          {
            iDecoderState = pVideoCodec->GetPicture(&picture);
          }

          if (iDecoderState == CDVDVideoCodec::VC_PICTURE)
          {
            if (!(picture.iFlags & DVP_FLAG_DROPPED))
              break;
          }

        } while (abort_index--);

        if (iDecoderState == CDVDVideoCodec::VC_PICTURE && !(picture.iFlags & DVP_FLAG_DROPPED))
        {
          const unsigned int maxWidth =
              CServiceBroker::GetSettingsComponent()->GetAdvancedSettings()->m_imageRes;
          result = CreateTextureFromPicture(picture, hint, maxWidth, 0);
        }
        else
        {
          CLog::LogF(LOGDEBUG, "decode failed in {} after {} packets.", redactPath, packetsTried);
        }
      }
    }
  }

  auto end = std::chrono::steady_clock::now();
  auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start);
  CLog::LogF(LOGDEBUG, "measured {} ms to extract thumb from file <{}> in {} packets. ",
             duration.count(), redactPath, packetsTried);

  return result;
}

bool CDVDFileInfo::CanExtract(const CFileItem& fileItem)
{
  if (fileItem.m_bIsFolder)
    return false;

  if (fileItem.IsLiveTV() ||
      // Due to a pvr addon api design flaw (no support for multiple concurrent streams
      // per addon instance), pvr recording thumbnail extraction does not work (reliably).
      URIUtils::IsPVRRecording(fileItem.GetDynPath()) ||
      // plugin path not fully resolved
      URIUtils::IsPlugin(fileItem.GetDynPath()) || URIUtils::IsUPnP(fileItem.GetPath()) ||
      fileItem.IsInternetStream() || fileItem.IsDiscStub() || fileItem.IsPlayList())
    return false;

  // mostly can't extract from discs and files from discs.
  if (URIUtils::IsBluray(fileItem.GetPath()) || fileItem.IsBDFile() || fileItem.IsDVD() ||
      fileItem.IsDiscImage() || fileItem.IsDVDFile(false, true))
    return false;

  // For HTTP/FTP we only allow extraction when on a LAN
  if (URIUtils::IsRemote(fileItem.GetPath()) && !URIUtils::IsOnLAN(fileItem.GetPath()) &&
      (URIUtils::IsFTP(fileItem.GetPath()) || URIUtils::IsHTTP(fileItem.GetPath())))
    return false;

  return true;
}

/**
 * \brief Open the item pointed to by pItem and extract streamdetails
 * \return true if the stream details have changed
 */
bool CDVDFileInfo::GetFileStreamDetails(CFileItem *pItem)
{
  if (!pItem)
    return false;

  if (!CanExtract(*pItem))
    return false;

  std::string strFileNameAndPath;
  if (pItem->HasVideoInfoTag())
    strFileNameAndPath = pItem->GetVideoInfoTag()->m_strFileNameAndPath;

  if (strFileNameAndPath.empty())
    strFileNameAndPath = pItem->GetDynPath();

  std::string playablePath = strFileNameAndPath;
  if (URIUtils::IsStack(playablePath))
    playablePath = XFILE::CStackDirectory::GetFirstStackedFile(playablePath);

  CFileItem item(playablePath, false);
  item.SetMimeTypeForInternetFile();
  auto pInputStream = CDVDFactoryInputStream::CreateInputStream(NULL, item);
  if (!pInputStream)
    return false;

  if (pInputStream->IsStreamType(DVDSTREAM_TYPE_PVRMANAGER))
  {
    return false;
  }

  if (pInputStream->IsStreamType(DVDSTREAM_TYPE_DVD) || !pInputStream->Open())
  {
    return false;
  }

  CDVDDemux *pDemuxer = CDVDFactoryDemuxer::CreateDemuxer(pInputStream, true);
  if (pDemuxer)
  {
    bool retVal = DemuxerToStreamDetails(pInputStream, pDemuxer, pItem->GetVideoInfoTag()->m_streamDetails, strFileNameAndPath);
    ProcessExternalSubtitles(pItem);
    delete pDemuxer;
    return retVal;
  }
  else
  {
    return false;
  }
}

bool CDVDFileInfo::DemuxerToStreamDetails(const std::shared_ptr<CDVDInputStream>& pInputStream,
                                          CDVDDemux* pDemuxer,
                                          const std::vector<CStreamDetailSubtitle>& subs,
                                          CStreamDetails& details)
{
  bool result = DemuxerToStreamDetails(pInputStream, pDemuxer, details);
  for (unsigned int i = 0; i < subs.size(); i++)
  {
    CStreamDetailSubtitle* sub = new CStreamDetailSubtitle();
    sub->m_strLanguage = subs[i].m_strLanguage;
    details.AddStream(sub);
    result = true;
  }
  return result;
}

/* returns true if details have been added */
bool CDVDFileInfo::DemuxerToStreamDetails(const std::shared_ptr<CDVDInputStream>& pInputStream,
                                          CDVDDemux* pDemux,
                                          CStreamDetails& details,
                                          const std::string& path)
{
  bool retVal = false;
  details.Reset();

  const CURL pathToUrl(path);
  for (CDemuxStream* stream : pDemux->GetStreams())
  {
    if (stream->type == STREAM_VIDEO && !(stream->flags & AV_DISPOSITION_ATTACHED_PIC))
    {
      CStreamDetailVideo *p = new CStreamDetailVideo();
      CDemuxStreamVideo* vstream = static_cast<CDemuxStreamVideo*>(stream);
      p->m_iWidth = vstream->iWidth;
      p->m_iHeight = vstream->iHeight;
      p->m_fAspect = static_cast<float>(vstream->fAspect);
      if (p->m_fAspect == 0.0f && p->m_iHeight > 0)
        p->m_fAspect = (float)p->m_iWidth / p->m_iHeight;
      p->m_strCodec = pDemux->GetStreamCodecName(stream->demuxerId, stream->uniqueId);
      p->m_iDuration = pDemux->GetStreamLength();
      p->m_strStereoMode = vstream->stereo_mode;
      p->m_strLanguage = vstream->language;
      p->m_strHdrType = CStreamDetails::HdrTypeToString(vstream->hdr_type);

      // stack handling
      if (URIUtils::IsStack(path))
      {
        CFileItemList files;
        XFILE::CStackDirectory stack;
        stack.GetDirectory(pathToUrl, files);

        // skip first path as we already know the duration
        for (int i = 1; i < files.Size(); i++)
        {
           int duration = 0;
           if (CDVDFileInfo::GetFileDuration(files[i]->GetDynPath(), duration))
             p->m_iDuration = p->m_iDuration + duration;
        }
      }

      // finally, calculate seconds
      if (p->m_iDuration > 0)
        p->m_iDuration = p->m_iDuration / 1000;

      details.AddStream(p);
      retVal = true;
    }

    else if (stream->type == STREAM_AUDIO)
    {
      CStreamDetailAudio *p = new CStreamDetailAudio();
      p->m_iChannels = static_cast<CDemuxStreamAudio*>(stream)->iChannels;
      p->m_strLanguage = stream->language;
      p->m_strCodec = pDemux->GetStreamCodecName(stream->demuxerId, stream->uniqueId);
      details.AddStream(p);
      retVal = true;
    }

    else if (stream->type == STREAM_SUBTITLE)
    {
      CStreamDetailSubtitle *p = new CStreamDetailSubtitle();
      p->m_strLanguage = stream->language;
      details.AddStream(p);
      retVal = true;
    }
  }  /* for iStream */

  details.DetermineBestStreams();
#ifdef HAVE_LIBBLURAY
  // correct bluray runtime. we need the duration from the input stream, not the demuxer.
  if (pInputStream->IsStreamType(DVDSTREAM_TYPE_BLURAY))
  {
    if (std::static_pointer_cast<CDVDInputStreamBluray>(pInputStream)->GetTotalTime() > 0)
    {
      const CStreamDetailVideo* dVideo = static_cast<const CStreamDetailVideo*>(details.GetNthStream(CStreamDetail::VIDEO, 0));
      CStreamDetailVideo* detailVideo = const_cast<CStreamDetailVideo*>(dVideo);
      if (detailVideo)
        detailVideo->m_iDuration = std::static_pointer_cast<CDVDInputStreamBluray>(pInputStream)->GetTotalTime() / 1000;
    }
  }
#endif
  return retVal;
}

void CDVDFileInfo::ProcessExternalSubtitles(CFileItem* item)
{
  std::vector<std::string> externalSubtitles;
  const std::string videoPath = item->GetDynPath();

  CUtil::ScanForExternalSubtitles(videoPath, externalSubtitles);

  for (const auto& externalSubtitle : externalSubtitles)
  {
    // if vobsub subtitle:
    if (URIUtils::GetExtension(externalSubtitle) == ".idx")
    {
      std::string subFile;
      if (CUtil::FindVobSubPair(externalSubtitles, externalSubtitle, subFile))
        AddExternalSubtitleToDetails(videoPath, item->GetVideoInfoTag()->m_streamDetails,
                                     externalSubtitle, subFile);
    }
    else
    {
      if (!CUtil::IsVobSub(externalSubtitles, externalSubtitle))
      {
        AddExternalSubtitleToDetails(videoPath, item->GetVideoInfoTag()->m_streamDetails,
                                     externalSubtitle);
      }
    }
  }
}

bool CDVDFileInfo::AddExternalSubtitleToDetails(const std::string &path, CStreamDetails &details, const std::string& filename, const std::string& subfilename)
{
  std::string ext = URIUtils::GetExtension(filename);
  std::string vobsubfile = subfilename;
  if(ext == ".idx")
  {
    if (vobsubfile.empty())
      vobsubfile = URIUtils::ReplaceExtension(filename, ".sub");

    CDVDDemuxVobsub v;
    if (!v.Open(filename, STREAM_SOURCE_NONE, vobsubfile))
      return false;

    for(CDemuxStream* stream : v.GetStreams())
    {
      CStreamDetailSubtitle *dsub = new CStreamDetailSubtitle();
      std::string lang = stream->language;
      dsub->m_strLanguage = g_LangCodeExpander.ConvertToISO6392B(lang);
      details.AddStream(dsub);
    }
    return true;
  }
  if(ext == ".sub")
  {
    std::string strReplace(URIUtils::ReplaceExtension(filename,".idx"));
    if (XFILE::CFile::Exists(strReplace))
      return false;
  }

  CStreamDetailSubtitle *dsub = new CStreamDetailSubtitle();
  ExternalStreamInfo info = CUtil::GetExternalStreamDetailsFromFilename(path, filename);
  dsub->m_strLanguage = g_LangCodeExpander.ConvertToISO6392B(info.language);
  details.AddStream(dsub);

  return true;
}
