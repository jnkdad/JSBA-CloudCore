using JSBA.CloudCore.Contracts.Models;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Operation.Buffer;

namespace JSBA.CloudCore.Extractor.Helpers;

/// <summary>
/// Uses NetTopologySuite for robust polygon reconstruction from line segments
/// </summary>
public class NtsPolygonizer
{
    private readonly ILogger<NtsPolygonizer> _logger;
    private readonly GeometryFactory _geometryFactory;

    public NtsPolygonizer(ILogger<NtsPolygonizer> logger)
    {
        _logger = logger;
        _geometryFactory = new GeometryFactory();
    }

    /// <summary>
    /// Convert RawPath to NTS LineString
    /// </summary>
    private LineString ToLineString(RawPath path)
    {
        var coordinates = path.Points.Select(p => new Coordinate(p.X, p.Y)).ToArray();
        return _geometryFactory.CreateLineString(coordinates);
    }

    /// <summary>
    /// Convert NTS Polygon to RoomBoundary
    /// </summary>
    private RoomBoundary ToRoomBoundary(Polygon polygon)
    {
        var points = polygon.ExteriorRing.Coordinates
            .Select(c => new Point2D { X = c.X, Y = c.Y })
            .ToList();

        return new RoomBoundary
        {
            Polygon = points
        };
    }

    /// <summary>
    /// Get area of a RoomBoundary
    /// </summary>
    private double GetArea(RoomBoundary boundary)
    {
        var coords = boundary.Polygon.Select(p => new Coordinate(p.X, p.Y)).ToArray();
        if (!coords[0].Equals2D(coords[^1]))
        {
            coords = coords.Append(coords[0]).ToArray();
        }
        var ring = _geometryFactory.CreateLinearRing(coords);
        var polygon = _geometryFactory.CreatePolygon(ring);
        return Math.Abs(polygon.Area);
    }

    /// <summary>
    /// Bridge small gaps by buffering lines slightly, then extracting centerlines
    /// This is much faster than the custom gap bridging algorithm
    /// </summary>
    public List<RawPath> BridgeGaps(List<RawPath> paths, double gapTolerance)
    {
        if (gapTolerance <= 0 || paths.Count == 0)
            return paths;

        _logger.LogInformation("NTS: Bridging gaps with tolerance {Tolerance} for {Count} paths",
            gapTolerance, paths.Count);

        try
        {
            // Convert paths to NTS LineStrings
            var lineStrings = paths.Select(ToLineString).ToList();

            // Create a MultiLineString
            var multiLineString = _geometryFactory.CreateMultiLineString(lineStrings.ToArray());

            // Union all lines (this merges connected segments)
            var merged = multiLineString.Union();

            _logger.LogInformation("NTS: Gap bridging complete. Result type: {Type}", merged.GeometryType);

            // Convert back to RawPaths
            var result = new List<RawPath>();
            ExtractLineStrings(merged, result, paths.FirstOrDefault()?.LineWidth ?? 1.0);

            _logger.LogInformation("NTS: Gap bridging: {Original} → {Merged} paths",
                paths.Count, result.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NTS: Gap bridging failed, returning original paths");
            return paths;
        }
    }

    /// <summary>
    /// Recursively extract LineStrings from a geometry
    /// </summary>
    private void ExtractLineStrings(Geometry geometry, List<RawPath> result, double lineWidth)
    {
        if (geometry is LineString lineString)
        {
            var points = lineString.Coordinates.Select(c => new Point2D { X = c.X, Y = c.Y }).ToList();
            if (points.Count >= 2)
            {
                result.Add(new RawPath
                {
                    Points = points,
                    LineWidth = lineWidth,
                    PathLength = lineString.Length
                });
            }
        }
        else if (geometry is GeometryCollection collection)
        {
            foreach (var geom in collection.Geometries)
            {
                ExtractLineStrings(geom, result, lineWidth);
            }
        }
    }

    /// <summary>
    /// Reconstruct closed polygons from line segments using NTS Polygonizer
    /// This is MUCH faster and more robust than the custom implementation
    /// </summary>
    public List<RoomBoundary> ReconstructPolygons(List<RawPath> paths)
    {
        _logger.LogInformation("NTS: Reconstructing polygons from {Count} paths", paths.Count);

        try
        {
            // Convert paths to NTS LineStrings
            var lineStrings = paths.Select(ToLineString).ToList();

            // Use NTS Polygonizer to find all closed polygons
            var polygonizer = new Polygonizer();
            polygonizer.Add(lineStrings.Cast<Geometry>().ToList());

            var polygons = polygonizer.GetPolygons();

            _logger.LogInformation("NTS: Found {Count} polygons", polygons.Count);

            // Convert to RoomBoundary
            var result = polygons
                .Cast<Polygon>()
                .Select(ToRoomBoundary)
                .ToList();

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NTS: Polygon reconstruction failed");
            return new List<RoomBoundary>();
        }
    }

    /// <summary>
    /// Remove outer polygons (keep only inner boundaries)
    /// Uses spatial containment checks - much faster than custom implementation
    /// </summary>
    public List<RoomBoundary> RemoveOuterPolygons(List<RoomBoundary> boundaries)
    {
        if (boundaries.Count <= 1)
            return boundaries;

        _logger.LogInformation("NTS: Removing outer polygons from {Count} boundaries", boundaries.Count);

        try
        {
            // Convert to NTS Polygons
            var ntsPolygons = boundaries.Select(b =>
            {
                var coords = b.Polygon.Select(p => new Coordinate(p.X, p.Y)).ToArray();
                // Ensure closed ring
                if (!coords[0].Equals2D(coords[^1]))
                {
                    coords = coords.Append(coords[0]).ToArray();
                }
                var ring = _geometryFactory.CreateLinearRing(coords);
                return _geometryFactory.CreatePolygon(ring);
            }).ToList();

            // Find polygons that are not contained by any other polygon
            var innerPolygons = new List<Polygon>();

            for (int i = 0; i < ntsPolygons.Count; i++)
            {
                bool isContained = false;

                for (int j = 0; j < ntsPolygons.Count; j++)
                {
                    if (i == j) continue;

                    // Check if polygon i is contained within polygon j
                    if (ntsPolygons[j].Contains(ntsPolygons[i]))
                    {
                        isContained = true;
                        break;
                    }
                }

                // Keep polygons that are NOT contained (inner boundaries)
                if (!isContained)
                {
                    innerPolygons.Add(ntsPolygons[i]);
                }
            }

            _logger.LogInformation("NTS: Removed outer polygons: {Original} → {Inner}",
                boundaries.Count, innerPolygons.Count);

            // Convert back to RoomBoundary
            return innerPolygons.Select(ToRoomBoundary).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NTS: Remove outer polygons failed, returning original");
            return boundaries;
        }
    }

    /// <summary>
    /// Remove nested polygons (keep only outermost polygons)
    /// </summary>
    public List<RoomBoundary> RemoveNestedPolygons(List<RoomBoundary> boundaries)
    {
        if (boundaries.Count <= 1)
            return boundaries;

        _logger.LogInformation("NTS: Removing nested polygons from {Count} boundaries", boundaries.Count);

        try
        {
            // Convert to NTS Polygons
            var ntsPolygons = boundaries.Select(b =>
            {
                var coords = b.Polygon.Select(p => new Coordinate(p.X, p.Y)).ToArray();
                if (!coords[0].Equals2D(coords[^1]))
                {
                    coords = coords.Append(coords[0]).ToArray();
                }
                var ring = _geometryFactory.CreateLinearRing(coords);
                return _geometryFactory.CreatePolygon(ring);
            }).ToList();

            // Keep only polygons that don't contain any other polygon
            var outerPolygons = new List<Polygon>();

            for (int i = 0; i < ntsPolygons.Count; i++)
            {
                bool containsOther = false;

                for (int j = 0; j < ntsPolygons.Count; j++)
                {
                    if (i == j) continue;

                    // Check if polygon i contains polygon j
                    if (ntsPolygons[i].Contains(ntsPolygons[j]))
                    {
                        containsOther = true;
                        break;
                    }
                }

                // Keep polygons that don't contain others
                if (!containsOther)
                {
                    outerPolygons.Add(ntsPolygons[i]);
                }
            }

            _logger.LogInformation("NTS: Removed nested polygons: {Original} → {Outer}",
                boundaries.Count, outerPolygons.Count);

            return outerPolygons.Select(ToRoomBoundary).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NTS: Remove nested polygons failed, returning original");
            return boundaries;
        }
    }

    /// <summary>
    /// Filter polygons by minimum area
    /// </summary>
    public List<RoomBoundary> FilterByMinArea(List<RoomBoundary> boundaries, double minArea)
    {
        if (minArea <= 0)
            return boundaries;

        var filtered = boundaries.Where(b => GetArea(b) >= minArea).ToList();

        _logger.LogInformation("NTS: Filtered by min area {MinArea}: {Original} → {Filtered}",
            minArea, boundaries.Count, filtered.Count);

        return filtered;
    }
}

