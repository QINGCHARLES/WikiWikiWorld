# WikiWikiWorld API Documentation

This document outlines the available REST API endpoints for the WikiWikiWorld application. 
Unless otherwise specified, APIs that require authentication must include a valid JWT token in the `Authorization` header.

```http
Authorization: Bearer <your_jwt_token>
```

---

## Authentication API

Base Path: `/api/auth`

### Issue Token
- **Endpoint**: `POST /api/auth/token`
- **Description**: Issues a JWT token in exchange for valid login credentials.
- **Request Body** (JSON):
  ```json
  {
      "Username": "your_username",
      "Password": "your_password"
  }
  ```
- **Responses**:
  - `200 OK`: Returns the JWT token string.
    ```json
    {
        "Token": "eyJhbGciOiJIUzI..."
    }
    ```
  - `400 Bad Request`: Validation failure (e.g., missing fields).
  - `401 Unauthorized`: Invalid credentials.

---

## Article API

Base Path: `/api/article`

### Get Article Revision
- **Endpoint**: `GET /api/article/{UrlSlug}`
- **Description**: Retrieves a specific or current article revision.
- **Query Parameters**:
  - `Revision` (optional string): Allows retrieving a specific revision by date string. If omitted, the latest current revision is returned.
- **Responses**:
  - `200 OK`: Returns the corresponding article revision object.
    ```json
    {
        "Id": 123,
        "CanonicalArticleId": "e12a4b3d-...",
        "SiteId": 1,
        "Culture": "en-US",
        "Title": "Article Title",
        "DisplayTitle": "Display Title",
        "UrlSlug": "article-title",
        "IsCurrent": true,
        "Type": "Article",
        "CanonicalFileId": null,
        "Text": "Markdown content here...",
        "RevisionReason": "Initial version",
        "CreatedByUserId": "f23b5c4e-...",
        "DateCreated": "2023-10-25T14:32:00+00:00",
        "DateDeleted": null
    }
    ```
  - `400 Bad Request`: Missing or invalid `UrlSlug`.
  - `404 Not Found`: No revision found for the specific criteria.

### Create Article Revision
- **Endpoint**: `POST /api/article/{UrlSlug}`
- **Description**: Creates a new article from scratch. If the article type is `File`, it can optionally accept a file upload in the same request.
- **Authorization**: Requires Bearer Token.
- **Request Format**: `multipart/form-data`
  - `Title` (required string)
  - `DisplayTitle` (optional string)
  - `Type` (required string): `"Article"`, `"Help"`, `"File"`, `"Project"`
  - `Text` (required string)
  - `RevisionReason` (required string)
  - `Source` (optional string): Used only if `Type` is `"File"`.
  - `File` (required file): The image file (required if `Type` is `"File"`, ignored otherwise).
- **Responses**:
  - `200 OK`: Article created successfully.
  - `400 Bad Request`: Missing slug, invalid parameters, missing file for File type.
  - `401 Unauthorized`: Missing or invalid token.
  - `409 Conflict`: An article with the slug already exists.

### Update Article Revision
- **Endpoint**: `PUT /api/article/{UrlSlug}`
- **Description**: Updates an existing article by creating a new revision and setting it as the current one.
- **Authorization**: Requires Bearer Token.
- **Request Body** (JSON):
  ```json
  {
      "CanonicalArticleId": "e12a4b3d-...", // optional Guid
      "Title": "Updated Title",
      "DisplayTitle": "Updated Display Title", // optional
      "Type": "Article",
      "CanonicalFileId": null, // optional Guid
      "Text": "Updated markdown content...",
      "RevisionReason": "Fixed typos"
  }
  ```
- **Responses**:
  - `200 OK`: Article revision updated successfully.
  - `400 Bad Request`: Missing slug or invalid parameters.
  - `401 Unauthorized`: Missing or invalid token.
  - `404 Not Found`: Article does not exist.

### Get Article History
- **Endpoint**: `GET /api/article/{UrlSlug}/history`
- **Description**: Retrieves a timeline of revisions for a given article, sorted by newest first.
- **Responses**:
  - `200 OK`: A JSON array of revision history entries.
    ```json
    [
        {
            "Id": 124,
            "Title": "Updated Title",
            "UrlSlug": "article-title",
            "IsCurrent": true,
            "RevisionReason": "Fixed typos",
            "CreatedByUserId": "f23b5c4e-...",
            "DateCreated": "2023-10-26T10:00:00+00:00"
        },
        {
            "Id": 123,
            "Title": "Article Title",
            "UrlSlug": "article-title",
            "IsCurrent": false,
            "RevisionReason": "Initial version",
            "CreatedByUserId": "f23b5c4e-...",
            "DateCreated": "2023-10-25T14:32:00+00:00"
        }
    ]
    ```
  - `404 Not Found`: Article not found.

### Delete Article
- **Endpoint**: `DELETE /api/article/{UrlSlug}`
- **Description**: Soft-deletes the current revision of an article.
- **Authorization**: Requires Bearer Token.
- **Responses**:
  - `200 OK`: Article deleted successfully.
  - `401 Unauthorized`: Missing or invalid token.
  - `404 Not Found`: Article not found.

---

## File API

Base Path: `/api/file`

### Upload File Revision
- **Endpoint**: `POST /api/file/{UrlSlug}`
- **Description**: Uploads an image file to the server and creates a new file revision for an existing File-type article. Validates that the uploaded file is a valid image format.
- **Authorization**: Requires Bearer Token.
- **Request Format**: `multipart/form-data`
  - `File`: The image file to be uploaded.
  - `Source` (optional string): The original source URL or attribution of the file.
  - `RevisionReason` (optional string): The reason for updating the file.
  - `Culture` (optional string): The culture of the source/reason text. Required if either `Source` or `RevisionReason` are provided.
- **Responses**:
  - `200 OK`: Returns metadata regarding the uploaded file.
    ```json
    {
        "CanonicalFileId": "...",
        "Filename": "...",
        "MimeType": "...",
        "FileSizeBytes": 12345
    }
    ```
  - `400 Bad Request`: No file uploaded, invalid image format, or missing Culture when Source/RevisionReason provided.
  - `401 Unauthorized`: Missing or invalid token.
  - `404 Not Found`: Article not found or article is not of type File.

### Get File Metadata
- **Endpoint**: `GET /api/file/{CanonicalFileId}`
- **Description**: Retrieves metadata for a file revision using its unique canonical file identifier.
- **Responses**:
  - `200 OK`: Returns the file's metadata object.
    ```json
    {
        "CanonicalFileId": "e55d...",
        "Filename": "image.jpg",
        "MimeType": "image/jpeg",
        "FileSizeBytes": 102400,
        "Type": "Image2D",
        "Source": "https://example.com/source-image",
        "DateCreated": "2023-10-25T14:32:00+00:00"
    }
    ```
  - `404 Not Found`: File not found.

---

## Search API

Base Path: `/api/search`

### Search Articles
- **Endpoint**: `GET /api/search`
- **Description**: Performs a site-wide search across both titles (prioritized) and content. Returns snippets of contextual text around the matched term.
- **Query Parameters**:
  - `Q` (required string): The search query.
- **Responses**:
  - `200 OK`: A list of search results.
    ```json
    [
        {
            "Title": "...",
            "UrlSlug": "...",
            "IsTitleMatch": true,
            "Snippet": null
        },
        {
            ...
        }
    ]
    ```
  - `400 Bad Request`: Missing query parameter.

---

## Common Workflows

### Creating a File Article with an Image

To create a new article that displays a file (e.g., an image upload), you can perform a single `POST` API call using `multipart/form-data`:

- **Endpoint**: `POST /api/article/{UrlSlug}`
- **Description**: Provide the standard article fields along with the `File` blob.
- **Form Fields**:
  - `Title`: "My New Image"
  - `Type`: "File"
  - `Text`: "This is a description of my image."
  - `RevisionReason`: "Initial upload"
  - `File`: *binary image data*

The system will automatically generate a `CanonicalFileId`, store the image, create the `FileRevision`, and bind it to the newly created `ArticleRevision`. To upload a replacement image later, use `POST /api/file/{UrlSlug}`.
