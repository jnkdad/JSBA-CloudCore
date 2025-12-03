# RoomExchange API – Phase 1 Specification
Version: 0.3  
Last Updated: 2025-12-03  
Author: Jerrold Stevens (JSBA)

---

## Overview

This document defines the API and DTO contract for RoomExchange Phase 1, supporting extraction of room geometry from PDF floor plans. This includes:

- A synchronous REST endpoint  
- The RIMJSON v0.3 format  
- Room geometry, polygon, and metadata fields  
- Example requests & responses  
- Error handling  
- Notes for integrators (CloudCore + StudioKrew)

This specification is the source of truth for all extraction-related development.

---

## Base URL

For development/testing, assume the following structure:

```
https://api.bimacoustics.cloudcore.com
```

(Exact host may vary depending on deployment environment.)

---

## Endpoints Summary

| Endpoint                     | Method | Description                         | Status     |
|------------------------------|--------|-------------------------------------|------------|
| `/api/rooms/extract`         | POST   | Extract rooms from a PDF            | REQUIRED   |
| `/api/rooms/extract/sample`  | GET    | Sample/health response              | OPTIONAL   |
| `/api/rooms/extract/dxf`     | GET    | Reserved for DXF output             | RESERVED   |

**Phase 1 requires only:** `POST /api/rooms/extract`

---

## POST `/api/rooms/extract`

Extracts room geometry from a PDF. Returns a structured JSON response in RIMJSON v0.3 format.

### Request

#### Method

```
POST /api/rooms/extract
```

#### Headers

```
Content-Type: multipart/form-data
Authorization: Bearer <token>
```

#### Body (multipart/form-data)

| Field        | Type    | Required | Description                         |
|--------------|---------|----------|-------------------------------------|
| `file`       | PDF     | Yes      | PDF file to extract rooms from.     |
| `pageIndex`  | Integer | No       | Page index (default: 0).            |
| `unitsHint`  | String  | No       | "feet" / "meters"                   |
| `projectId`  | String  | No       | Tracking identifier                 |

#### Example

```
------boundary
Content-Disposition: form-data; name="file"; filename="Level_1.pdf"
Content-Type: application/pdf

%PDF-1.6...
------boundary
Content-Disposition: form-data; name="pageIndex"

0
------boundary--
```

---

## Response (200 OK)

#### Content-Type

```
application/json
```

#### Envelope (RIMJSON v0.3)

```json
{
  "version": "0.3",
  "source": {
    "fileName": "Level_1.pdf",
    "pageIndex": 0,
    "units": "feet"
  },
  "rooms": [
    {
      "id": "R-001",
      "name": "101 Classroom",
      "number": "101",
      "levelName": "Level 1",
      "area": 780.25,
      "perimeter": 112.0,
      "ceilingHeight": 9.0,
      "boundingBox": {
        "minX": 10.0,
        "minY": 20.0,
        "maxX": 40.0,
        "maxY": 50.0
      },
      "polygon": [
        { "x": 10.0, "y": 20.0 },
        { "x": 40.0, "y": 20.0 },
        { "x": 40.0, "y": 50.0 },
        { "x": 10.0, "y": 50.0 }
      ],
      "metadata": {
        "pdfLayer": "A-ROOM",
        "confidence": 0.97
      }
    }
  ]
}
```

---

## DTO Reference (C#)

```csharp
public class RoomDto
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Number { get; set; }
    public string LevelName { get; set; }

    public double Area { get; set; }
    public double Perimeter { get; set; }
    public double CeilingHeight { get; set; }

    public BoundingBox2D BoundingBox { get; set; }
    public List<Point2D> Polygon { get; set; }

    public Dictionary<string, object> Metadata { get; set; }
}

public class BoundingBox2D
{
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
}

public class Point2D
{
    public double X { get; set; }
    public double Y { get; set; }
}
```

---

## Error Responses

#### Format

```json
{
  "error": {
    "code": "INVALID_PDF",
    "message": "The uploaded file is not a valid PDF document."
  }
}
```

#### Common Error Codes

| Code                 | Meaning                                       |
|----------------------|-----------------------------------------------|
| `MISSING_FILE`       | No PDF provided                               |
| `INVALID_PDF`        | File could not be parsed                      |
| `EXTRACTION_FAILED`  | Internal extractor error                      |
| `UNSUPPORTED_FORMAT` | PDF contains unsupported structure            |

---

## Integration Notes

### CloudCore (Mahmood)

- Implement `/api/rooms/extract` endpoint  
- Invoke RoomExchange extractor  
- Map extractor output to RIMJSON v0.3  
- Return JSON synchronously  
- Placeholder for `/dxf` (Phase 2)

### StudioKrew (Vineet)

- Upload PDF → receive JSON  
- Display rooms list + geometry in UI  
- Use metadata where helpful  
- DXF integration in Phase 2

### Extractor (Yang)

- Detect polygons and bounding boxes  
- Produce consistent 2D coordinates  
- Provide metadata (confidence, layer, etc.)  
- Ensure extractor output maps cleanly to schema

---

## Reserved (Phase 2)

### GET `/api/rooms/extract/dxf`

_Not implemented in Phase 1._

```
GET /api/rooms/extract/dxf?jobId={jobId}
```

Returns DXF representation of room boundaries.

---

## Version History

| Version | Date       | Notes                             |
|---------|------------|-----------------------------------|
| 0.3     | 2025-12-03 | Initial API + DTO specification   |

---

_End of Document_
