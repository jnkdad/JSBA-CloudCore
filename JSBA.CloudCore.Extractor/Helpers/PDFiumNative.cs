using System.Runtime.InteropServices;

namespace JSBA.CloudCore.Extractor.Helpers;

/// <summary>
/// Native P/Invoke bindings for PDFium library
/// Based on PDFium from https://github.com/bblanchon/pdfium-binaries
/// </summary>
public static class PDFiumNative
{
    private const string DllName = "pdfium.dll";

    #region Library Initialization

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDF_InitLibrary();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDF_DestroyLibrary();

    #endregion

    #region Document Functions

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint FPDF_LoadMemDocument(byte[] data_buf, int size, string password);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDF_CloseDocument(nint document);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDF_GetPageCount(nint document);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint FPDF_GetLastError();

    #endregion

    #region Page Functions

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint FPDF_LoadPage(nint document, int page_index);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDF_ClosePage(nint page);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern double FPDF_GetPageWidth(nint page);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern double FPDF_GetPageHeight(nint page);

    #endregion

    #region Page Object Functions

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFPage_CountObjects(nint page);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint FPDFPage_GetObject(nint page, int index);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFPageObj_GetType(nint page_object);

    /// <summary>
    /// Get the bounding box of a page object
    /// Returns true on success, false on failure
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool FPDFPageObj_GetBounds(nint page_object, out float left, out float bottom, out float right, out float top);

    #endregion

    #region Path Object Functions

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFPath_CountSegments(nint path);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint FPDFPath_GetPathSegment(nint path, int index);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool FPDFPathSegment_GetPoint(nint segment, out float x, out float y);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFPathSegment_GetType(nint segment);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool FPDFPathSegment_GetClose(nint segment);

    /// <summary>
    /// Get the drawing mode of a path object
    /// fillmode: 0 for no fill, 1 for alternate, 2 for winding
    /// stroke: true if stroke, false otherwise
    /// Returns true on success
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool FPDFPath_GetDrawMode(nint path, out int fillmode, out bool stroke);

    /// <summary>
    /// Get the stroke width of a page object
    /// Returns true on success
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool FPDFPageObj_GetStrokeWidth(nint pageObject, out float width);

    #endregion

    #region Text Extraction Functions

    /// <summary>
    /// Prepare information about all characters in a page
    /// Returns a handle to the text page information structure
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint FPDFText_LoadPage(nint page);

    /// <summary>
    /// Release all resources allocated for a text page information structure
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDFText_ClosePage(nint text_page);

    /// <summary>
    /// Get number of characters in a page
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFText_CountChars(nint text_page);

    /// <summary>
    /// Get Unicode of a character in a page
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint FPDFText_GetUnicode(nint text_page, int index);

    /// <summary>
    /// Get bounding box of a particular character
    /// Returns true on success, false on failure
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool FPDFText_GetCharBox(nint text_page, int index, out double left, out double right, out double bottom, out double top);

    /// <summary>
    /// Extract unicode text string from the page
    /// buffer: A buffer (allocated by application) receiving the extracted text
    /// start_index: Index for the start character
    /// count: Number of characters to be extracted
    /// Returns number of characters written into the buffer (including trailing zeros)
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFText_GetText(nint text_page, int start_index, int count, [Out] byte[] buffer);

    /// <summary>
    /// Get font size of a particular character
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern double FPDFText_GetFontSize(nint text_page, int index);

    #endregion

    #region Constants

    // Page object types
    public const int FPDF_PAGEOBJ_UNKNOWN = 0;
    public const int FPDF_PAGEOBJ_TEXT = 1;
    public const int FPDF_PAGEOBJ_PATH = 2;
    public const int FPDF_PAGEOBJ_IMAGE = 3;
    public const int FPDF_PAGEOBJ_SHADING = 4;
    public const int FPDF_PAGEOBJ_FORM = 5;

    // Path segment types
    public const int FPDF_SEGMENT_UNKNOWN = -1;
    public const int FPDF_SEGMENT_LINETO = 0;
    public const int FPDF_SEGMENT_BEZIERTO = 1;
    public const int FPDF_SEGMENT_MOVETO = 2;

    // Error codes
    public const uint FPDF_ERR_SUCCESS = 0;
    public const uint FPDF_ERR_UNKNOWN = 1;
    public const uint FPDF_ERR_FILE = 2;
    public const uint FPDF_ERR_FORMAT = 3;
    public const uint FPDF_ERR_PASSWORD = 4;
    public const uint FPDF_ERR_SECURITY = 5;
    public const uint FPDF_ERR_PAGE = 6;

    #endregion
}

