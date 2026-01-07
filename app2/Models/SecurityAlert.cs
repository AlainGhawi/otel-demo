// ==========================================================================
// Copyright (C) 2026 by Genetec, Inc.
// All rights reserved.
// May be used only in accordance with a valid Source Code License Agreement.
// ==========================================================================

namespace AlertService.Models;

public record SecurityAlert(
    Guid Id,
    string Type,
    string Source,
    string Severity,
    string Message,
    DateTime Timestamp,
    string Status,
    object? Metadata = null,
    string? AcknowledgedBy = null,
    DateTime? AcknowledgedAt = null,
    string? Resolution = null,
    DateTime? ResolvedAt = null
);