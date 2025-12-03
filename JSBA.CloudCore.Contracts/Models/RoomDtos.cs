// RoomDtos.cs
//   RoomExchange DTOs - Public API contract
// These are the canonical DTOs for external parties (e.g., Mamun's façade)

using System.Collections.Generic;

namespace JSBA.CloudCore.Contracts.Models
{
    /// <summary>
    ///   RoomExchange response envelope
    /// </summary>
    public class RoomsResponseDto
    {
        public List<RoomDto> Rooms { get; set; } = new();
    }

    /// <summary>
    ///   RoomExchange room DTO
    /// Stable identifier and geometry for room extraction
    /// </summary>
    public class RoomDto
    {
        /// <summary>
        /// Stable identifier for the room within this extraction job
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Optional human-readable info (if we can infer or derive it)
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Optional room number (extracted from name if possible)
        /// </summary>
        public string? Number { get; set; }

        /// <summary>
        /// Optional level/floor (extracted from name if possible)
        /// </summary>
        public string? Level { get; set; }

        /// <summary>
        /// Room area in the same units as the source plan (unitless from the DTO's perspective)
        /// </summary>
        public double Area { get; set; }

        /// <summary>
        /// Plan-view boundary polygon, ordered vertex list
        /// </summary>
        public List<PointDto> Polygon { get; set; } = new();

        /// <summary>
        /// Geometric centroid of the polygon in the same 2D coordinate system as Polygon
        /// </summary>
        public PointDto? Centroid { get; set; }
    }

    /// <summary>
    ///   RoomExchange point DTO
    /// </summary>
    public class PointDto
    {
        public double X { get; set; }
        public double Y { get; set; }
    }
}

