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
        /// Uploads a PDF floor plan and returns extracted room geometry (RIMJSON v0.3)
        /// </summary>
        /// <param name="file">PDF floor plan file</param>
        /// <param name="pageIndex">Page index (default: 0)</param>
        /// <param name="unitsHint">Units hint: "feet" or "meters" (default: "feet")</param>
        /// <param name="projectId">Project identifier for tracking</param>
        /// <returns>Room extraction results in RIMJSON v0.3 format</returns>
        [HttpPost("extract")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(RoomsResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status500InternalServerError)]
        public IActionResult ExtractRooms(
            IFormFile file, 
            [FromForm] int? pageIndex = null,
            [FromForm] string? unitsHint = null,
            [FromForm] string? projectId = null)
        {
            try
            {
                // Validate file
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new ErrorResponseDto
                    {
                        Error = new ErrorInfoDto
                        {
                            Code = "MISSING_FILE",
                            Message = "No PDF file provided or file is empty."
                        }
                    });
                }

                // Validate file type
                if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new ErrorResponseDto
                    {
                        Error = new ErrorInfoDto
                        {
                            Code = "INVALID_PDF",
                            Message = "The uploaded file is not a valid PDF document."
                        }
                    });
                }

                _logger.LogInformation("Processing PDF file: {FileName}, Size: {Size} bytes, PageIndex: {PageIndex}, Units: {Units}", 
                    file.FileName, file.Length, pageIndex ?? 0, unitsHint ?? "feet");

                // Process the PDF (returns RIMJSON v0.3 DTO)
                using var stream = file.OpenReadStream();
                var pdfOptions = new PdfOptions
                {
                    FileName = file.FileName,
                    PageIndex = pageIndex,
                    UnitsHint = unitsHint,
                    ProjectId = projectId
                };
                
                var result = _pdfExtractor.ProcessPdfToRooms(stream, pdfOptions);

                _logger.LogInformation("Successfully extracted {RoomCount} rooms from {FileName}", result.Rooms.Count, file.FileName);

                return Ok(result);
            }
            catch (NotImplementedException)
            {
                _logger.LogWarning("PDF extraction not yet implemented");
                return StatusCode(StatusCodes.Status501NotImplemented, new ErrorResponseDto
                {
                    Error = new ErrorInfoDto
                    {
                        Code = "EXTRACTION_FAILED",
                        Message = "PDF extraction not yet fully implemented."
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing PDF file");
                
                // Determine error code based on exception type
                string errorCode = "EXTRACTION_FAILED";
                string errorMessage = "An internal error occurred while processing the PDF.";
                
                // Check if it's a PDF parsing error
                if (ex.Message.Contains("PDF", StringComparison.OrdinalIgnoreCase) || 
                    ex.Message.Contains("parse", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("invalid", StringComparison.OrdinalIgnoreCase))
                {
                    errorCode = "INVALID_PDF";
                    errorMessage = "The uploaded file could not be parsed as a PDF document.";
                }
                else if (ex.Message.Contains("format", StringComparison.OrdinalIgnoreCase) ||
                         ex.Message.Contains("unsupported", StringComparison.OrdinalIgnoreCase))
                {
                    errorCode = "UNSUPPORTED_FORMAT";
                    errorMessage = "The PDF contains an unsupported structure or format.";
                }

                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponseDto
                {
                    Error = new ErrorInfoDto
                    {
                        Code = errorCode,
                        Message = errorMessage
                    }
                });
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

