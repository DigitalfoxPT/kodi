/*
 *  Copyright (C) 2005-2018 Team Kodi
 *  This file is part of Kodi - https://kodi.tv
 *
 *  SPDX-License-Identifier: GPL-2.0-or-later
 *  See LICENSES/README.md for more information.
 */

#include "GUIDialogSeekPreview.h"

CGUIDialogSeekPreview::CGUIDialogSeekPreview()
  : CGUIDialog(WINDOW_DIALOG_SEEK_PREVIEW, "DialogSeekPreview.xml", DialogModalityType::MODELESS)
{
  // The root visibility condition opens and closes this dialog automatically.
  // Loading it during GUI initialisation makes that condition independent of
  // DialogSeekBar and of whichever fullscreen controls happen to be visible.
  m_loadType = LOAD_ON_GUI_INIT;
}

bool CGUIDialogSeekPreview::OnAction(const CAction& /*action*/)
{
  // This is a visual-only overlay. Let all remote-control actions continue to
  // the fullscreen player underneath it.
  return false;
}
