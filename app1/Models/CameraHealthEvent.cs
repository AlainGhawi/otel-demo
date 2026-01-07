// ==========================================================================
// Copyright (C) 2026 by Genetec, Inc.
// All rights reserved.
// May be used only in accordance with a valid Source Code License Agreement.
// ==========================================================================

namespace CameraGateway.Models;

public record CameraHealthEvent(string CameraId, bool IsOnline, string? ErrorMessage = null);