// RoomDtos.cs
// RoomExchange DTOs - Public API contract (RIMJSON v0.3)
// These are the canonical DTOs for external parties (e.g., Mamun's façade)

using System.Collections.Generic;

namespace JSBA.CloudCore.Contracts.Models
{
    /// <summary>
    /// RoomExchange response envelope (RIMJSON v0.3)
    /// </summary>
    public class RoomsResponseDto
    {
        /// <summary>
        /// RIMJSON version
        /// </summary>
        public string Version { get; set; } = "0.3";

        /// <summary>
        /// Source document information
        /// </summary>
        public SourceInfoDto Source { get; set; } = new();

        /// <summary>
        /// Extracted rooms
        /// </summary>
        public List<RoomDto> Rooms { get; set; } = new();
    }

    /// <summary>
    /// Source document information
    /// </summary>
    public class SourceInfoDto
    {
        public string FileName { get; set; } = string.Empty;
        public int PageIndex { get; set; } = 0;
        public string Units { get; set; } = "feet";
    }

    /// <summary>
    /// RoomExchange room DTO (RIMJSON v0.3)
    /// </summary>
    public class RoomDto
    {
        /// <summary>
        /// Stable identifier for the room within this extraction job
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable room name
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Room number (extracted from name if possible)
        /// </summary>
        public string? Number { get; set; }

        /// <summary>
        /// Level/floor name (extracted from name if possible)
        /// </summary>
        public string? LevelName { get; set; }

        /// <summary>
        /// Room area in the same units as the source plan
        /// </summary>
        public double Area { get; set; }

        /// <summary>
        /// Room perimeter in the same units as the source plan
        /// </summary>
        public double Perimeter { get; set; }

        /// <summary>
        /// Ceiling height (if available)
        /// </summary>
        public double? CeilingHeight { get; set; }

        /// <summary>
        /// Bounding box of the room
        /// </summary>
        public BoundingBox2DDto? BoundingBox { get; set; }

    /// <summary>
    /// Plan-view boundary polygon, ordered vertex list
    /// </summary>
    public List<Point2D> Polygon { get; set; } = new();

        /// <summary>
        /// Additional metadata (confidence, pdfLayer, etc.)
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }
    }

    /// <summary>
    /// Bounding box in 2D space
    /// </summary>
    public class BoundingBox2DDto
    {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
    }

    /// <summary>
    /// Point in 2D space (RIMJSON v0.3)
    /// </summary>
    public class Point2D
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    /// <summary>
    /// Error response DTO (RIMJSON v0.3)
    /// </summary>
    public class ErrorResponseDto
    {
        public ErrorInfoDto Error { get; set; } = new();
    }

    /// <summary>
    /// Error information
    /// </summary>
    public class ErrorInfoDto
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}

