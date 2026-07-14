# CloudStore - Existing Application

This is a complete, functional CRUD application for managing products. It demonstrates:

- **ASP.NET Core Web API** (.NET 10) with PostgreSQL
- **Angular Single-Page Application** (v22) frontend
- **Redis caching** for API responses
- **Azure Blob Storage** for product images
- **Azure Table Storage** queue for image processing notifications
- **Local development** using Docker Compose and Azurite emulators

## Architecture

```text
CloudStore.Api              - RESTful API (.NET 10)
CloudStore.Web              - Angular 22 frontend (SPA)
CloudStore.Infrastructure   - Shared data access and services
Docker Compose              - PostgreSQL, Redis, Azurite containers
```

## Prerequisites

- **.NET 10 SDK** (Minimum) ([Download](https://dotnet.microsoft.com/download/dotnet/10.0))
- **Node.js 20+** and **npm** ([Download](https://nodejs.org/))
- **Docker Desktop** (running) ([Download](https://www.docker.com/products/docker-desktop))
- A code editor (VS Code, Visual Studio, etc.)

## Getting Started

### 1. Start Local Services

From the project root, start Docker containers:

```bash
docker-compose up -d
```

This starts:

- **PostgreSQL** (localhost:5432)
- **Redis** (localhost:6379)
- **Azurite** (localhost:10000-10002 for Blob/Queue/Table storage)

Verify services are running:

```bash
docker-compose ps
```

### 2. Build and Run the API

From the `CloudStore.Api` project root:

```bash
# Restore packages and build
dotnet build

# Run the API (applies migrations automatically on first run)
dotnet run --project CloudStore.Api.csproj
```

The API will be available at `http://localhost:5200`

**Output should show:**

```text
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5200
```

You can test the API by visiting `http://localhost:5200/api/products` in your browser or using Postman.


### 3. Build and Run the Frontend

In a separate terminal, from the project root:

```bash
cd CloudStore.Web

# Install dependencies
npm install

# Start the development server
npm start
```

The frontend will be available at `http://localhost:4200`

### 4. Verify Everything Works

1. Open `http://localhost:4200` in your browser
2. You should see three seeded products: Laptop, Mechanical Keyboard, 4K Monitor
3. **Test CRUD operations:**
   - Try updating a product name
   - Create a new product
   - Delete a product
4. **Test image upload:**
   - Select a product
   - Click "Upload Image"
   - Choose an image file
5. **Verify caching** (optional):
   - Watch the API logs for cache hits/misses

## What's Inside

### API Endpoints

- `GET /api/products` - List all products (cached)
- `GET /api/products/{id}` - Get product details
- `POST /api/products` - Create a product
- `PUT /api/products/{id}` - Update a product
- `DELETE /api/products/{id}` - Delete a product
- `POST /api/products/{id}/upload-image` - Upload product image to Azure Storage

### Local Services

| Service       | URL/Port        | Purpose                |
|---------------|-----------------|------------------------|
| PostgreSQL    | localhost:5432  | Product database       |
| Redis         | localhost:6379  | Response caching       |
| Azurite Blob  | localhost:10000 | Product image storage  |
| Azurite Table | localhost:10002 | Image processing queue |

### Data Flow

1. **Product Operations**: API ↔ PostgreSQL
2. **Caching**: API ↔ Redis
3. **Image Upload**: API → Azurite Blob Storage
4. **Queue Notification**: Azurite Table Storage (for future Azure Function processing)

## Stopping Services

```bash
# Stop Docker containers
docker-compose down

# To remove volumes too (cleans database)
docker-compose down -v
```

## Troubleshooting

### "Cannot connect to PostgreSQL"

- Ensure Docker Desktop is running: `docker info`
- Restart containers: `docker-compose restart`

### "Cannot connect to API from frontend"

- Check API is running on port 7200
- Verify CORS is enabled in `CloudStore.Api/Program.cs`

### "Image uploads fail"

- Ensure the Azurite container is running
- Check container logs: `docker-compose logs azurite`

## Next Steps

This application is the starting point for **Section 03 Labs**, where you'll:

1. **Lab 1**: Create an Aspire AppHost to orchestrate these services
2. **Lab 2**: Add the Angular frontend to AppHost
3. **Lab 3**: Replace docker-compose PostgreSQL with Aspire-managed PostgreSQL
4. **Lab 4**: Replace hardcoded Redis connection with Aspire-managed Redis
5. **Lab 5**: Remove docker-compose entirely, add Aspire Azure Storage integration
6. **Lab 6**: Create an Azure Function for thumbnail generation
7. **Lab 7**: Add health checks, observability, and custom dashboard commands

Start with **Lab 1** at `../lab-01-add-apphost.md`
