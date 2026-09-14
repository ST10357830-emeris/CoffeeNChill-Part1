# CoffeeNChill Part 1

Azure Functions isolated worker for the CoffeeNChill menu and staff-document APIs. The application uses Azure Table Storage for menu items and Azure Blob Storage for staff documents. Local development uses Azurite.

## Requirements

- .NET 8 SDK
- Azure Functions Core Tools v4
- Docker Desktop
- Postman

### Docker command not found on Windows

If PowerShell reports `docker: The term 'docker' is not recognized`, Docker Desktop may have been installed after the current VS Code window was opened. Close and reopen VS Code so its integrated terminal reloads the user `PATH`, then verify:

```powershell
docker version
```

If an existing terminal must be reused, refresh its `PATH` for the current session:

```powershell
$dockerDir = "$env:LOCALAPPDATA\Programs\DockerDesktop\resources\bin"
$env:Path = "$dockerDir;$env:Path"
docker version
```

The Docker CLI is installed at `$env:LOCALAPPDATA\Programs\DockerDesktop\resources\bin\docker.exe`. Docker Desktop must be running before `docker pull`, `docker build`, or `docker run` commands are used.

## Two-terminal Docker run

Use these commands if the current VS Code terminal still reports that `docker` cannot be found. The `$docker` variable uses the absolute Docker Desktop CLI path and does not depend on the terminal `PATH`.

Terminal 1, Azurite:

```powershell
$docker = "$env:LOCALAPPDATA\Programs\DockerDesktop\resources\bin\docker.exe"
$env:Path = "$env:LOCALAPPDATA\Programs\DockerDesktop\resources\bin;$env:Path"
& $docker pull mcr.microsoft.com/azure-storage/azurite
& $docker rm -f coffeenchill-azurite 2>$null
& $docker run --name coffeenchill-azurite -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite
```

Terminal 2, Functions:

```powershell
$docker = "$env:LOCALAPPDATA\Programs\DockerDesktop\resources\bin\docker.exe"
$env:Path = "$env:LOCALAPPDATA\Programs\DockerDesktop\resources\bin;$env:Path"
dotnet publish --configuration Release --no-restore
& $docker build -t khwinana/coffeenchill-functions:v1.1 .
& $docker rm -f coffeenchill-functions 2>$null
& $docker run --name coffeenchill-functions -p 7071:80 khwinana/coffeenchill-functions:v1.1
```

After restarting VS Code, the equivalent commands can use `docker` directly because the Docker Desktop directory will be loaded into the new terminal's `PATH`.

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
docker build -t <dockerhub-username>/coffeenchill-functions:v1.1 .
```

The Functions Dockerfile contains the Azurite connection string using `host.docker.internal`, so no connection string or `--network` option is needed when starting the Functions container:

```powershell
docker run --name coffeenchill-functions -p 7071:80 <dockerhub-username>/coffeenchill-functions:v1.1
```

The `staff-docs` Blob container is created automatically when the upload, list, or download endpoint is first called.

Push the image after replacing the Docker Hub username:

```powershell
docker login
docker push <dockerhub-username>/coffeenchill-functions:v1.1
```

Published image: https://hub.docker.com/r/khwinana/coffeenchill-functions/tags

The current rubric-complete build is also tagged `v1.1`:

```powershell
docker tag <dockerhub-username>/coffeenchill-functions:v1.1 <dockerhub-username>/coffeenchill-functions:latest
docker push <dockerhub-username>/coffeenchill-functions:latest
```

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

Run the `Menu` folder in this order: create, get all, category filter, update, delete, then invalid menu item. Run the `Documents` folder in this order: upload a PDF or text file, list documents, download the uploaded blob, then run the unsupported document type request with a `.gif` file. The saved tests verify success responses, structured 400 errors, metadata, and Blob MIME headers.

## Demonstration video sequence

1. Show the repository, Dockerfile, Postman collection folders, and Docker Hub tags `v1.0` and `v1.1`.
2. Run Azurite in its own container with `docker run` and show ports `10000`, `10001`, and `10002`.
3. Run the Functions image independently with `docker run -p 7071:80` and no connection-string argument.
4. Show the Functions host listing all eight HTTP functions.
5. Run the five menu requests in Postman and show the saved tests passing.
6. Upload a PDF or text document and show the response containing filename, size, content type, and upload time.
7. List documents and show size, content type, upload time, and last modified time.
8. Download the blob and show the response content type and downloaded file.
9. Run the invalid menu, missing item, and unsupported MIME requests and show the 400/404 tests passing.
10. Show the public GitHub repository and Docker Hub image page.

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