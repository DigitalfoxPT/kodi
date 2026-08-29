/*
 *  Copyright (C) 2005-2026 Team Kodi
 *  This file is part of Kodi - https://kodi.tv
 *
 *  SPDX-License-Identifier: GPL-2.0-or-later
 *  See LICENSES/README.md for more information.
 */

#pragma once

class CVideoDatabase;

namespace VIDEO
{
class CVideoSeekPreviewCache
{
public:
  /*! \brief Synchronize Android TV seek-preview images with the video library.
   \param database Open video database after a scan or clean operation.
   \param generateMissing Whether missing ten-second images should also be generated.
   */
  static void Update(CVideoDatabase& database, bool generateMissing);
};
} // namespace VIDEO
