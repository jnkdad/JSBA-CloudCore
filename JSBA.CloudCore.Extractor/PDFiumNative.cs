using System.Runtime.InteropServices;

namespace JSBA.CloudCore.Extractor;

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
    public static extern IntPtr FPDF_LoadMemDocument(byte[] data_buf, int size, string password);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDF_CloseDocument(IntPtr document);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDF_GetPageCount(IntPtr document);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint FPDF_GetLastError();

    #endregion

    #region Page Functions

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDF_LoadPage(IntPtr document, int page_index);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDF_ClosePage(IntPtr page);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern double FPDF_GetPageWidth(IntPtr page);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern double FPDF_GetPageHeight(IntPtr page);

    #endregion

    #region Page Object Functions

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFPage_CountObjects(IntPtr page);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDFPage_GetObject(IntPtr page, int index);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFPageObj_GetType(IntPtr page_object);

    /// <summary>
    /// Get the bounding box of a page object
    /// Returns true on success, false on failure
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool FPDFPageObj_GetBounds(IntPtr page_object, out float left, out float bottom, out float right, out float top);

    #endregion

    #region Path Object Functions

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFPath_CountSegments(IntPtr path);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDFPath_GetPathSegment(IntPtr path, int index);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool FPDFPathSegment_GetPoint(IntPtr segment, out float x, out float y);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFPathSegment_GetType(IntPtr segment);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool FPDFPathSegment_GetClose(IntPtr segment);

    /// <summary>
    /// Get the drawing mode of a path object
    /// fillmode: 0 for no fill, 1 for alternate, 2 for winding
    /// stroke: true if stroke, false otherwise
    /// Returns true on success
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool FPDFPath_GetDrawMode(IntPtr path, out int fillmode, out bool stroke);

    /// <summary>
    /// Get the stroke width of a page object
    /// Returns true on success
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool FPDFPageObj_GetStrokeWidth(IntPtr pageObject, out float width);

    #endregion

    #region Text Extraction Functions

    /// <summary>
    /// Prepare information about all characters in a page
    /// Returns a handle to the text page information structure
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDFText_LoadPage(IntPtr page);

    /// <summary>
    /// Release all resources allocated for a text page information structure
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDFText_ClosePage(IntPtr text_page);

    /// <summary>
    /// Get number of characters in a page
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFText_CountChars(IntPtr text_page);

    /// <summary>
    /// Get Unicode of a character in a page
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint FPDFText_GetUnicode(IntPtr text_page, int index);

    /// <summary>
    /// Get bounding box of a particular character
    /// Returns true on success, false on failure
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool FPDFText_GetCharBox(IntPtr text_page, int index, out double left, out double right, out double bottom, out double top);

    /// <summary>
    /// Extract unicode text string from the page
    /// buffer: A buffer (allocated by application) receiving the extracted text
    /// start_index: Index for the start character
    /// count: Number of characters to be extracted
    /// Returns number of characters written into the buffer (including trailing zeros)
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFText_GetText(IntPtr text_page, int start_index, int count, [Out] byte[] buffer);

    /// <summary>
    /// Get font size of a particular character
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern double FPDFText_GetFontSize(IntPtr text_page, int index);

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

