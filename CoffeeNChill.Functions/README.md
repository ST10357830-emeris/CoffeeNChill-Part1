# CoffeeNChill Part 1

Azure Functions isolated worker for the CoffeeNChill menu and staff-document APIs. The application uses Azure Table Storage for menu items and Azure Blob Storage for staff documents. Local development uses Azurite.

## Requirements

- .NET 8 SDK
- Azure Functions Core Tools v4
- Docker Desktop
- Postman

## Run locally with Azurite

Start Azurite as a standalone container. Azurite's default ports are Blob `10000`, Queue `10001`, and Table `10002`:

```powershell
docker pull mcr.microsoft.com/azure-storage/azurite
docker run --name coffeenchill-azurite -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite
```

The checked-in `local.settings.json` uses `UseDevelopmentStorage=true`, which is suitable when Functions Core Tools runs on the host. Start the Functions project with:

```powershell
dotnet build
func start
```

The API is available at `http://localhost:7071/api`.

## Run the Functions container

Publish the project, then build the standalone image from this directory:

```powershell
dotnet publish --configuration Release
docker build -t <dockerhub-username>/coffeenchill-functions:v1.0 .
```

The Functions Dockerfile contains the Azurite connection string using `host.docker.internal`, so no connection string or `--network` option is needed when starting the Functions container:

```powershell
docker run --name coffeenchill-functions -p 7071:80 <dockerhub-username>/coffeenchill-functions:v1.0
```

The `staff-docs` Blob container is created automatically when the upload, list, or download endpoint is first called.

Push the image after replacing the Docker Hub username:

```powershell
docker login
docker push <dockerhub-username>/coffeenchill-functions:v1.0
```

Published image: https://hub.docker.com/r/khwinana/coffeenchill-functions/tags

For the assignment demonstration, use the official Azurite image directly. If your team must publish a tagged copy, tag and push it as `<dockerhub-username>/coffeenchill-azurite:v1.0`.

## Endpoints

| Method | Route | Purpose |
| --- | --- | --- |
| POST | `/api/menu` | Create a menu item |
| GET | `/api/menu` | List all menu items |
| GET | `/api/menu/category/{category}` | Filter menu items by category |
| PUT | `/api/menu/{category}/{id}` | Update price, availability, name, or description |
| DELETE | `/api/menu/{category}/{id}` | Delete a menu item |
| POST | `/api/documents/upload` | Upload a multipart document |
| GET | `/api/documents` | List document name, size, and modified time |
| GET | `/api/documents/download/{fileName}` | Download a document |

Example menu item JSON:

```json
{
  "partitionKey": "Hot Drinks",
  "rowKey": "COF-001",
  "name": "Espresso",
  "description": "Double-shot espresso",
  "price": 2.5,
  "isAvailable": true
}
```

## Postman

Import `docs/CoffeeNChill-Part1.postman_collection.json` into Postman. Set `baseUrl` to `http://localhost:7071/api` and set `filePath` to a local PDF or document before running the upload request. The collection covers every required menu and document endpoint.

## Submission checklist

- Commit and push the repository using each student's individual GitHub account.
- Keep at least five meaningful commits per student for this part.
- Add the public Docker Hub image link above or in the project submission notes.
- Add the unlisted demonstration video link to this README after recording the voiceover and Postman run.
- Record team contributions below.

### Team contributions

| Team member | Contribution |
| --- | --- |
| Add name | Add contribution |
| Add name | Add contribution |

### Demonstration video

Add unlisted YouTube link here: `https://youtu.be/REPLACE_WITH_VIDEO_ID`