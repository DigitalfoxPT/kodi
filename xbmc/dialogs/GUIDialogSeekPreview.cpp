/*
 *  Copyright (C) 2005-2018 Team Kodi
 *  This file is part of Kodi - https://kodi.tv
 *
 *  SPDX-License-Identifier: GPL-2.0-or-later
 *  See LICENSES/README.md for more information.
 */

#include "GUIDialogSeekPreview.h"

#include "ServiceBroker.h"
#include "application/ApplicationComponents.h"
#include "application/ApplicationPlayer.h"

CGUIDialogSeekPreview::CGUIDialogSeekPreview()
  : CGUIDialog(WINDOW_DIALOG_SEEK_PREVIEW, "DialogSeekPreview.xml", DialogModalityType::MODELESS)
{
  // The root visibility condition opens and closes this dialog automatically.
  // Loading it during GUI initialisation makes that condition independent of
  // DialogSeekBar and of whichever fullscreen controls happen to be visible.
  m_loadType = LOAD_ON_GUI_INIT;
}

bool CGUIDialogSeekPreview::OnAction(const CAction& action)
{
  // Once the preview is visible, route remote actions to the seek handler
  // before the fullscreen window or OSD can consume OK/Back. Unsupported
  // actions still fall through to the normal fullscreen player.
  const auto& components = CServiceBroker::GetAppComponents();
  const auto appPlayer = components.GetComponent<CApplicationPlayer>();
  if (appPlayer->GetSeekHandler().IsSeekPreviewActive())
    return appPlayer->GetSeekHandler().OnAction(action);

  return false;
}
