# CoffeeNChill Part 1

Azure Functions isolated worker for the CoffeeNChill menu and staff-document APIs. The application uses Azure Table Storage for menu items and Azure Files for staff documents. Local development uses Azurite.

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

Build the standalone image from this directory:

```powershell
docker build -t <dockerhub-username>/coffeenchill-functions:v1.0 .
```

When the Functions container connects to the Azurite container through Docker Desktop on Windows, use `host.docker.internal` in the storage endpoints:

```powershell
$storage = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFeqCnf2g==;BlobEndpoint=http://host.docker.internal:10000/devstoreaccount1;QueueEndpoint=http://host.docker.internal:10001/devstoreaccount1;TableEndpoint=http://host.docker.internal:10002/devstoreaccount1;FileEndpoint=http://host.docker.internal:10000/devstoreaccount1;"
docker run --name coffeenchill-functions -p 7071:80 -e AzureWebJobsStorage=$storage -e FUNCTIONS_WORKER_RUNTIME=dotnet-isolated <dockerhub-username>/coffeenchill-functions:v1.0
```

Push the image after replacing the Docker Hub username:

```powershell
docker login
docker push <dockerhub-username>/coffeenchill-functions:v1.0
```

Publish the Azurite image reference used by the demonstration as `<dockerhub-username>/coffeenchill-azurite:v1.0` only if your team tags and pushes a copy of the official image.

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