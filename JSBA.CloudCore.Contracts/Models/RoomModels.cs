// RoomModels.cs
// Data models for the CloudCore PDF→Rooms extraction engine.
// Public API contract - only models used by external parties

using System.Collections.Generic;

namespace JSBA.CloudCore.Contracts.Models
{
    /// <summary>
    /// Metadata about the extraction result
    /// </summary>
    public class RoomsMetadata
    {
        public string Units { get; set; } = "feet";
        public string? Scale { get; set; }
        public int PageCount { get; set; } = 1;
    }

    /// <summary>
    /// Options for PDF extraction
    /// </summary>
    public class PdfOptions
    {
        /// <summary>
        /// Optional PDF filename for loading PDF-specific extraction settings
        /// </summary>
        public string? FileName { get; set; }

        // placeholder for future configuration (page index, tolerances, etc.)
    }
}
