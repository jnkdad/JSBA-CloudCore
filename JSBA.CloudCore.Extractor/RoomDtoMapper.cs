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
        /// Map internal RoomsResult to public RoomsResponseDto
        /// </summary>
        public static RoomsResponseDto MapToResponseDto(RoomsResult internalResult)
        {
            return new RoomsResponseDto
            {
                Rooms = internalResult.Rooms.Select(MapToRoomDto).ToList()
            };
        }

        /// <summary>
        /// Map internal RoomModel to public RoomDto
        /// Computes Area and Centroid, extracts Number and Level
        /// </summary>
        public static RoomDto MapToRoomDto(RoomModel room)
        {
            var polygon = room.Polygon;
            var centroid = CalculateCentroid(polygon);
            var area = CalculateArea(polygon);

            // Extract Number and Level from Name if possible
            ExtractRoomInfo(room.Name, out string? number, out string? level);

            return new RoomDto
            {
                Id = room.Id,
                Name = room.Name,
                Number = number,
                Level = level,
                Area = area,
                Polygon = polygon.Select(p => new PointDto { X = p.X, Y = p.Y }).ToList(),
                Centroid = new PointDto { X = centroid.X, Y = centroid.Y }
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
        /// Calculate centroid of a polygon
        /// </summary>
        private static Point2D CalculateCentroid(List<Point2D> polygon)
        {
            if (polygon.Count == 0)
                return new Point2D { X = 0, Y = 0 };

            double sumX = 0, sumY = 0;
            foreach (var point in polygon)
            {
                sumX += point.X;
                sumY += point.Y;
            }

            return new Point2D
            {
                X = sumX / polygon.Count,
                Y = sumY / polygon.Count
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

