// RoomDtoMapper.cs
// Maps internal RoomModel to public RoomDto (  RoomExchange DTO)
// Computes Area and Centroid, extracts Number and Level from Name

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JSBA.CloudCore.Contracts.Models;
using JSBA.CloudCore.Extractor.Helpers;
using NetTopologySuite.Geometries;

namespace JSBA.CloudCore.Extractor
{
    /// <summary>
    /// Maps internal extraction models to RoomExchange DTOs
    /// </summary>
    internal static class RoomDtoMapper
    {
        private static readonly GeometryFactory _geometryFactory = new GeometryFactory();

        /// <summary>
        /// Map internal RoomsResult to public RoomsResponseDto (RIMJSON v0.3)
        /// </summary>
        public static RoomsResponseDto MapToResponseDto(RoomsResult internalResult, string fileName, int pageIndex = 0, string units = "feet")
        {
            return new RoomsResponseDto
            {
                Version = "0.3",
                Source = new SourceInfoDto
                {
                    FileName = fileName,
                    PageIndex = pageIndex,
                    Units = units
                },
                Rooms = internalResult.Rooms.Select(MapToRoomDto).ToList()
            };
        }

        /// <summary>
        /// Map internal RoomModel to public RoomDto (RIMJSON v0.3)
        /// Computes Area, Perimeter, BoundingBox, extracts Number and LevelName
        /// </summary>
        public static RoomDto MapToRoomDto(RoomModel room)
        {
            var polygon = room.Polygon;
            var area = CalculateArea(polygon);
            var perimeter = CalculatePerimeter(polygon);
            var boundingBox = CalculateBoundingBox(polygon);

            // Extract Number and LevelName from Name if possible
            ExtractRoomInfo(room.Name, out string? number, out string? levelName);

            // Build metadata dictionary (if additional properties are available in RoomModel, add them here)
            var metadata = new Dictionary<string, object>();
            // Note: Confidence and PdfLayer are not currently in RoomModel, but can be added later

            return new RoomDto
            {
                Id = room.Id,
                Name = room.Name,
                Number = number,
                LevelName = levelName,
                Area = area,
                Perimeter = perimeter,
                CeilingHeight = null, // Not available from extraction
                BoundingBox = boundingBox,
                Polygon = polygon.Select(p => new Point2DDto { X = p.X, Y = p.Y }).ToList(),
                Metadata = metadata.Count > 0 ? metadata : null
            };
        }

        /// <summary>
        /// Calculate polygon area using NTS (same method as NtsPolygonizer)
        /// </summary>
        private static double CalculateArea(List<Point2D> polygon)
        {
            if (polygon.Count < 3)
                return 0;

            var coords = polygon.Select(p => new Coordinate(p.X, p.Y)).ToArray();
            // Ensure closed ring
            if (!coords[0].Equals2D(coords[^1]))
            {
                coords = coords.Append(coords[0]).ToArray();
            }
            var ring = _geometryFactory.CreateLinearRing(coords);
            var ntsPolygon = _geometryFactory.CreatePolygon(ring);
            return Math.Abs(ntsPolygon.Area);
        }

        /// <summary>
        /// Calculate perimeter of a polygon
        /// </summary>
        private static double CalculatePerimeter(List<Point2D> polygon)
        {
            if (polygon.Count < 2)
                return 0;

            double perimeter = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                var current = polygon[i];
                var next = polygon[(i + 1) % polygon.Count];
                var dx = next.X - current.X;
                var dy = next.Y - current.Y;
                perimeter += Math.Sqrt(dx * dx + dy * dy);
            }

            return perimeter;
        }

        /// <summary>
        /// Calculate bounding box of a polygon
        /// </summary>
        private static BoundingBox2DDto? CalculateBoundingBox(List<Point2D> polygon)
        {
            if (polygon.Count == 0)
                return null;

            double minX = polygon[0].X, minY = polygon[0].Y;
            double maxX = polygon[0].X, maxY = polygon[0].Y;

            foreach (var point in polygon)
            {
                if (point.X < minX) minX = point.X;
                if (point.X > maxX) maxX = point.X;
                if (point.Y < minY) minY = point.Y;
                if (point.Y > maxY) maxY = point.Y;
            }

            return new BoundingBox2DDto
            {
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY
            };
        }

        /// <summary>
        /// Extract room number and level from room name
        /// Examples: "ROOM 101" -> Number="101", Level=null
        ///           "LEVEL 2 ROOM 205" -> Number="205", Level="2"
        ///           "2F-101" -> Number="101", Level="2"
        /// </summary>
        private static void ExtractRoomInfo(string? name, out string? number, out string? level)
        {
            number = null;
            level = null;

            if (string.IsNullOrWhiteSpace(name))
                return;

            // Try to extract level patterns: "LEVEL X", "X FLOOR", "XF", "X-F", etc.
            var levelPatterns = new[]
            {
                new Regex(@"LEVEL\s+(\d+)", RegexOptions.IgnoreCase),
                new Regex(@"(\d+)\s*F(?:LOOR)?", RegexOptions.IgnoreCase),
                new Regex(@"(\d+)[\s-]F", RegexOptions.IgnoreCase),
                new Regex(@"^(\d+)[\s-]", RegexOptions.IgnoreCase) // Starts with number followed by dash/space
            };

            foreach (var pattern in levelPatterns)
            {
                var match = pattern.Match(name);
                if (match.Success)
                {
                    level = match.Groups[1].Value;
                    break;
                }
            }

            // Try to extract room number: "ROOM XXX", "XXX", "RXXX", etc.
            var numberPatterns = new[]
            {
                new Regex(@"ROOM\s+(\d+[A-Z]?)", RegexOptions.IgnoreCase),
                new Regex(@"R\s*(\d+[A-Z]?)", RegexOptions.IgnoreCase),
                new Regex(@"(\d{2,}[A-Z]?)", RegexOptions.IgnoreCase) // 2+ digits optionally followed by letter
            };

            foreach (var pattern in numberPatterns)
            {
                var match = pattern.Match(name);
                if (match.Success)
                {
                    var candidate = match.Groups[1].Value;
                    // If we already found a level that matches this, skip it
                    if (level == null || !candidate.StartsWith(level))
                    {
                        number = candidate;
                        break;
                    }
                }
            }

            // If no number found but we have a level, try to extract number after level
            if (number == null && level != null)
            {
                var afterLevelPattern = new Regex($@"{Regex.Escape(level)}[\s-]+(\d+[A-Z]?)", RegexOptions.IgnoreCase);
                var match = afterLevelPattern.Match(name);
                if (match.Success)
                {
                    number = match.Groups[1].Value;
                }
            }
        }
    }
}

