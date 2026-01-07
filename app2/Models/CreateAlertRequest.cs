// ==========================================================================
// Copyright (C) 2026 by Genetec, Inc.
// All rights reserved.
// May be used only in accordance with a valid Source Code License Agreement.
// ==========================================================================

namespace AlertService.Models;

public record CreateAlertRequest(
    string Type,
    string Source,
    string Severity,
    string Message,
    DateTime? Timestamp = null,
    object? Metadata = null
);