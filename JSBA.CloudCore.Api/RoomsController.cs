using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace JSBA.CloudCore.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RoomsController : ControllerBase
    {
        // POST: /api/rooms/extract
        // Staging endpoint: ignores uploaded PDF and returns sample data
        [HttpPost("extract")]
        public ActionResult<RoomsResponse> ExtractRooms([FromForm] RoomExtractRequest request)
        {
            var sampleResponse = new RoomsResponse
            {
                Rooms = new List<RoomDto>
                {
                    new RoomDto
                    {
                        Id = "01-101",
                        Name = "Classroom",
                        Number = "101",
                        Level = "Level 01",
                        Area = 345.22,
                        Polygon = new List<PointDto>
                        {
                            new PointDto { X = 0.0, Y = 0.0 },
                            new PointDto { X = 34.46, Y = 0.0 },
                            new PointDto { X = 34.46, Y = 27.57 },
                            new PointDto { X = 0.0, Y = 27.57 }
                        },
                        Centroid = new PointDto { X = 17.23, Y = 13.78 }
                    }
                }
            };

            return Ok(sampleResponse);
        }

        // GET: /api/rooms/test
        // Quick browser test endpoint
        [HttpGet("test")]
        public ActionResult<RoomsResponse> Test()
        {
            return ExtractRooms(new RoomExtractRequest()).Result;
        }
    }

    // Request wrapper
    public class RoomExtractRequest
    {
        [FromForm(Name = "file")]
        public IFormFile? File { get; set; }
    }

    // Response wrapper
    public class RoomsResponse
    {
        public List<RoomDto> Rooms { get; set; } = new();
    }

    // Data model
    public class RoomDto
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Number { get; set; }
        public string? Level { get; set; }
        public double Area { get; set; }
        public List<PointDto> Polygon { get; set; } = new();
        public PointDto? Centroid { get; set; }
    }

    public class PointDto
    {
        public double X { get; set; }
        public double Y { get; set; }
    }
}
