// RoomModels.cs
// Internal room models used during PDF extraction processing
// These are not part of the public API contract

using System.Collections.Generic;
using JSBA.CloudCore.Contracts.Models; // For RoomsMetadata

namespace JSBA.CloudCore.Extractor
{
    /// <summary>
    /// Internal result from PDF extraction processing
    /// </summary>
    public class RoomsResult
    {
        public List<RoomModel> Rooms { get; set; } = new();
        public RoomsMetadata Metadata { get; set; } = new();
    }

    /// <summary>
    /// Internal room model used during extraction
    /// </summary>
    public class RoomModel
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public List<Point2D> Polygon { get; set; } = new();
    }
}

