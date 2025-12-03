// RoomsController.cs
// REST API endpoint for PDF→Rooms extraction

using Microsoft.AspNetCore.Mvc;
using JSBA.CloudCore.Contracts.Models;
using JSBA.CloudCore.Contracts.Interfaces;
using JSBA.CloudCore.Contracts.Exceptions;
using System;
using System.IO;
using System.Linq;

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
            IFormFile? file, 
            [FromForm] int? pageIndex = null,
            [FromForm] string? unitsHint = null,
            [FromForm] string? projectId = null)
        {
            // Handle missing file before model validation
            if (file == null)
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

            try
            {
                // Validate file - check if it's empty
                if (file.Length == 0)
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
            catch (PdfExtractionException ex)
            {
                _logger.LogError(ex, "PDF extraction error: {ErrorCode}", ex.ErrorCode);
                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponseDto
                {
                    Error = new ErrorInfoDto
                    {
                        Code = ex.ErrorCode,
                        Message = ex.Message
                    }
                });
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
                _logger.LogError(ex, "Unexpected error processing PDF file");
                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponseDto
                {
                    Error = new ErrorInfoDto
                    {
                        Code = "EXTRACTION_FAILED",
                        Message = "An internal error occurred while processing the PDF."
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

