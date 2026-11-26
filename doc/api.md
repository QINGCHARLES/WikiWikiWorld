# WikiWikiWorld API Documentation

This document outlines the available REST API endpoints for the WikiWikiWorld application. 
Unless otherwise specified, APIs that require authentication must include a valid JWT token in the `Authorization` header.

```http
Authorization: Bearer <your_jwt_token>
```

### Multi-Site Targeting & `scripts/wiki_api.py`

Because a single WikiWikiWorld deployment serves multiple sites and language editions, every API endpoint resolves its `SiteId` and `Culture` from the request's `Host` header (for example `en.magazedia.com`, `en.cinepedia.wiki`, `en.magazedia.com.localhost:7126`, or `en.cinepedia.wiki.localhost:7126`).

When using `scripts/wiki_api.py`, pass `--host <hostname>` (or set the `WIKI_API_HOST` environment variable) to target a specific site or culture:

```bash
# Default targets Magazedia (en.magazedia.com in prod, en.magazedia.com.localhost in dev)
python scripts/wiki_api.py request dev GET api/article/wired

# Target Cinepedia in dev or prod
python scripts/wiki_api.py request dev --host en.cinepedia.wiki.localhost GET api/article/the-matrix
python scripts/wiki_api.py request prod --host en.cinepedia.wiki GET api/article/the-matrix
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
  - `DisplayTitle` (optional string): the page heading. Plain text, not Markdown; see [Display titles](#display-titles).
  - `Type` (required string): `"Article"`, `"Category"`, `"File"`, `"User"`, `"Info"`
  - `Text` (required string)
  - `RevisionReason` (required string)
  - `Source` (optional string): Used only if `Type` is `"File"`.
  - `File` (required file): The image file (required if `Type` is `"File"`, ignored otherwise).
- **Responses**:
  - `200 OK`: `{"Message": "Article created successfully.", "Warnings": []}`. `Warnings` lists problems to fix, with line numbers: infobox problems (such as a key the site's infobox schema does not define), email addresses, YouTube addresses or raw channel IDs in the text (use `{{YouTubeLink}}`), and `{{YouTubeLink}}` directives that do not validate. They never stop the save. Read them after every save and fix the article. See [Save warnings](../MARKDIG_EXTENSIONS.md#save-warnings).
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
      "Type": "Article", // one of "Article", "Category", "File", "User", "Info"
      "CanonicalFileId": null, // optional Guid
      "Text": "Updated markdown content...",
      "RevisionReason": "Fixed typos",
      "NewUrlSlug": "new-slug" // optional: renames the article
  }
  ```
- **Display title**: `DisplayTitle` follows the same rules as on create; see [Display titles](#display-titles).
- **Renaming**: Set `NewUrlSlug` to move the article to a new address. The new revision keeps the same `CanonicalArticleId`, so history, discussions and bookmarks follow it; earlier revisions keep the old slug. Omit it or leave it empty to keep the current slug. A `file:` prefix is ignored, as for lookups. Afterwards the old address answers with a temporary `302` redirect to the new one, unless another current article takes the old slug, which then wins. (It is not a `301`, because browsers cache those and slugs can be reused.) Links to the old slug in other articles' text, including `file:` references and `{{CoverGrid}}` lists, are not rewritten; `file:` and CoverGrid lookups do not follow the redirect.
- **Responses**:
  - `200 OK`: `{"Message": "Article revision updated successfully.", "Warnings": []}`, with `Warnings` as for create.
  - `400 Bad Request`: Missing slug or invalid parameters, or `NewUrlSlug` is a reserved system path.
  - `401 Unauthorized`: Missing or invalid token.
  - `404 Not Found`: Article does not exist.
  - `409 Conflict`: `NewUrlSlug` is already used by another current article.

### Display titles
`DisplayTitle` exists for the article's header, where a title can spread over several lines or show a logo. Write it for that place only. Everywhere else, including the browser tab, search results, the HTML sitemap and the upload page, the article is named by `Title`. The only other page that shows it is the article's Links page, which reuses the header.

**It is plain text, not Markdown.** Nothing formats it, so `*`, `_`, quote marks and HTML tags (including `<br>`) appear literally in the heading. Do not italicise it, even for a publication name. Publication names are italic in the article body, and Wikipedia italicises them in page titles, but neither convention applies to this field. Example: the lead sentence can say `***Stash*** is…`, but the display title is `Stash`, never `*Stash*`.

Leave it empty, or the same as `Title`, unless you need one of the two things it supports:

- **Line breaks**, written as a real newline (`\n` in JSON). Use one in place of the ` - ` separators in a title, so the heading breaks cleanly instead of wrapping at a stray hyphen. For example, the title `Outlander - Issue 01` gets the display title `"Outlander\nIssue 01"`.
- **Logo tokens**: `[[Fallback Text|#|file-slug]]` shows the current image from the `File` article `file-slug` in place of the text, and falls back to `Fallback Text` if the file is missing. A token alone on its line is shown as a block logo. Example: `"[[Outlander|#|outlander-logo]]\nIssue 01"`.
  - **Monochrome mask (default)**: `[[Outlander|#|outlander-logo]]` paints the file's alpha silhouette in `currentColor`.
  - **Multi-color / unmasked**: `[[GQ|#|gq-logo|#|color]]` renders an unmasked `<img>` so multi-color, badge, and multi-tone shaded SVGs retain their fills (embedded SVGs inherit the heading's `color-scheme` for internal `@media (prefers-color-scheme: dark)` or `light-dark()` styles).
  - **Light and dark logo pair**: `[[Mojo|#|mojo-logo-light|~|mojo-logo-dark]]` (or `[[Mojo|#|mojo-logo-light, mojo-logo-dark]]`) specifies separate light-mode and dark-mode `File` slugs within the file slot. Pairs render unmasked by default and switch with the active `color-scheme` (append `|#|mask` if both files are monochrome masks); both files must exist or the token falls back to `Fallback Text`.

Slashes get optional break points automatically. After saving, `GET` the article and check that `DisplayTitle` contains no `*`, `_` or `<`.

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
- **Authorization**: Requires Bearer Token, and the `DeleteArticles` permission (Senior Editor or Administrator) for anything other than the caller's own `@user` page. See [roles](roles-and-trusted-domains.md).
- **Responses**:
  - `200 OK`: Article deleted successfully.
  - `401 Unauthorized`: Missing or invalid token.
  - `403 Forbidden`: Another account's user page, or the caller lacks the `DeleteArticles` permission.
  - `404 Not Found`: Article not found.

---

## Trusted Domains API

Base Path: `/api/trusted-domains`. Requires a Bearer token. Trust is global across sites. Links to a trusted domain, or to any of its subdomains, render without `nofollow`. A proposal waits until an administrator confirms or denies it at `/Admin/TrustedDomains`. Members can also propose at `/wiki:trusted-domains`. See [roles and trusted link domains](roles-and-trusted-domains.md).

### List Trusted Domains and Your Proposals
- **Endpoint**: `GET /api/trusted-domains`
- **Response** `200 OK`:
  ```json
  {
    "Trusted": ["gq.com", "vogue.com"],
    "MyProposals": [
      { "Domain": "si.edu", "Status": "Pending", "DateProposed": "2026-11-24T06:07:05.55+00:00", "AdministratorNote": null }
    ]
  }
  ```
  `Status` is `Pending`, `Trusted`, `Denied` or `Revoked`. `AdministratorNote` is the reviewer's note, or the reason trust was withdrawn. `MyProposals` lists the caller's latest 50 proposals.

### Propose a Domain
- **Endpoint**: `POST /api/trusted-domains`
- **Body** (`application/json`): `{ "Domain": "https://www.vogue.com/about", "Reason": "Official site of Vogue magazine" }`. `Domain` may be a bare domain or any address on the site; it is reduced to the host, lowercase, without `www.`. `Reason` is required and at most 1,000 characters.
- **Responses**:
  - `200 OK`: `{ "Domain": "vogue.com", "Status": "Pending" }`.
  - `400 Bad Request`: the domain is not valid (an IP address, a bare top-level domain such as `gov`, or not a hostname), or the reason is missing or too long.
  - `409 Conflict`: the domain is already trusted, directly or through a parent domain, or is already waiting for review. There is nothing to do.
- Messages are in English on every culture.

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
        "CanonicalFileId": "e12a4b3d-...",
        "Filename": "image.jpg",
        "MimeType": "image/jpeg",
        "FileSizeBytes": 12345,
        "ImageWidthPixels": 1920,
        "ImageHeightPixels": 1080
    }
    ```
  - `400 Bad Request`: No file uploaded, invalid image format, or missing Culture when Source/RevisionReason provided.
  - `401 Unauthorized`: Missing or invalid token.
  - `404 Not Found`: Article not found or article is not of type File.

### Get File Metadata
- **Endpoint**: `GET /api/file/{UrlSlug}`
- **Description**: Retrieves metadata for the file article identified by the given slug.
- **Responses**:
  - `200 OK`: Returns the file's metadata object.
    ```json
    {
        "Filename": "image.jpg",
        "MimeType": "image/jpeg",
        "FileSizeBytes": 102400,
        "Type": "Image2D",
        "Source": "https://example.com/source-image",
        "ImageWidthPixels": 1920,
        "ImageHeightPixels": 1080,
        "DateCreated": "2023-10-25T14:32:00+00:00",
        "Url": "/sitefiles/1/123/image.jpg"
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
