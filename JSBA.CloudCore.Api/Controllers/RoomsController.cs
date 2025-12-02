// RoomsController.cs
// REST API endpoint for PDF→Rooms extraction

using Microsoft.AspNetCore.Mvc;
using JSBA.CloudCore.Contracts.Models;
using JSBA.CloudCore.Contracts.Interfaces;
using System;
using System.IO;

namespace JSBA.CloudCore.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RoomsController : ControllerBase
    {
        private readonly IPdfExtractor _pdfExtractor;
        private readonly ILogger<RoomsController> _logger;

        public RoomsController(IPdfExtractor pdfExtractor, ILogger<RoomsController> logger)
        {
            _pdfExtractor = pdfExtractor;
            _logger = logger;
        }

        /// <summary>
        /// Uploads a PDF floor plan and returns extracted room geometry
        /// </summary>
        /// <param name="file">PDF floor plan file</param>
        /// <param name="options">Optional JSON string for future configuration</param>
        /// <returns>Room extraction results</returns>
        [HttpPost("extract")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(RoomsResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult ExtractRooms(IFormFile file, [FromForm] string? options = null)
        {
            try
            {
                // Validate file
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new { error = "No file uploaded or file is empty" });
                }

                // Validate file type
                if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { error = "Invalid file type. Only PDF files are supported." });
                }

                _logger.LogInformation("Processing PDF file: {FileName}, Size: {Size} bytes", file.FileName, file.Length);

                // Process the PDF
                using var stream = file.OpenReadStream();
                var pdfOptions = new PdfOptions
                {
                    FileName = file.FileName // Pass filename for PDF-specific settings lookup
                };
                var result = _pdfExtractor.ProcessPdfToRooms(stream, pdfOptions);

                _logger.LogInformation("Successfully extracted {RoomCount} rooms from {FileName}", result.Rooms.Count, file.FileName);

                return Ok(result);
            }
            catch (NotImplementedException)
            {
                _logger.LogWarning("PDF extraction not yet implemented");
                return StatusCode(StatusCodes.Status501NotImplemented, new { error = "PDF extraction not yet fully implemented" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing PDF file");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while processing the PDF", details = ex.Message });
            }
        }

        /// <summary>
        /// Quick browser test endpoint
        /// </summary>
        [HttpGet("test")]
        public IActionResult Test()
        {
            return Ok(new { status = "healthy", service = "CloudCore PDF Extraction API" });
        }
    }
}

