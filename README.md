# API Timeout Handler

A .NET 8 RESTful API solution that handles external API timeouts using Kafka event generation, MongoDB caching, and background processing strategies.

## 🎯 Problem Statement

This solution addresses the common issue of external API timeouts by implementing:

1. **Scheduled API Calls**: Background service runs at 1 AM daily to make API calls at a steady pace
2. **Intelligent Caching**: MongoDB-based caching with 1-hour windows
3. **Timeout Fallback**: Returns cached data when external API times out
4. **Event-Driven Architecture**: Uses Kafka for scheduling and retry mechanisms

## 🏗️ Architecture

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   API Client    │───▶│  API Controller │───▶│ External API    │
└─────────────────┘    └─────────────────┘    └─────────────────┘
                                │                        │
                                ▼                        │
                       ┌─────────────────┐               │
                       │   MongoDB       │               │
                       │   (Cache)       │               │
                       └─────────────────┘               │
                                │                        │
                                ▼                        │
                       ┌─────────────────┐               │
                       │     Kafka       │               │
                       │  (Events)       │               │
                       └─────────────────┘               │
                                │                        │
                                ▼                        │
                       ┌─────────────────┐               │
                       │  Background     │───────────────┘
                       │   Service       │
                       └─────────────────┘
```

## 🚀 Features

- **Smart Caching**: 1-hour cache window with automatic expiration
- **Background Processing**: Daily scheduled API calls at 1 AM
- **Timeout Handling**: Graceful fallback to cached data
- **Event-Driven**: Kafka-based event scheduling and retry logic
- **Rate Limiting**: Configurable delays between API calls
- **Comprehensive Logging**: Structured logging with Serilog
- **Health Monitoring**: Built-in health checks
- **Docker Support**: Complete containerization setup

## 🛠️ Technology Stack

- **.NET 8**: Latest .NET framework
- **MongoDB**: Document database for caching
- **Apache Kafka**: Event streaming platform
- **Serilog**: Structured logging
- **Docker**: Containerization
- **Swagger**: API documentation

## 📋 Prerequisites

- .NET 8 SDK
- Docker and Docker Compose
- MongoDB (via Docker)
- Apache Kafka (via Docker)

## 🚀 Quick Start

### 1. Clone and Setup

```bash
git clone <repository-url>
cd ApiTimeoutHandler
```

### 2. Start Infrastructure

```bash
# Start MongoDB and Kafka
docker-compose up -d mongodb kafka zookeeper

# Optional: Start monitoring tools
docker-compose up -d kafka-ui mongo-express
```

### 3. Run the Application

```bash
# Restore dependencies
dotnet restore

# Run the application
dotnet run
```

### 4. Access the API

- **API**: `https://localhost:5001` or `http://localhost:5000`
- **Swagger UI**: `https://localhost:5001/swagger`
- **Health Check**: `https://localhost:5001/health`
- **Kafka UI**: `http://localhost:8080` (if started)
- **MongoDB Express**: `http://localhost:8081` (if started)

## 📚 API Endpoints

### Get API Data
```http
GET /api/ApiData/{apiId}
```

**Behavior:**
- Returns cached data if available and fresh (< 1 hour)
- Calls external API if cache miss or stale data
- Falls back to cached data if external API fails
- Schedules retry events for timeouts

**Response:**
```json
{
  "success": true,
  "data": { /* API response data */ },
  "isFromCache": false,
  "lastUpdated": "2024-01-15T10:30:00Z",
  "responseTimeMs": 150,
  "message": "Data retrieved from external API"
}
```

### Schedule API Call
```http
POST /api/ApiData/schedule/{apiId}
Content-Type: application/json

{
  "scheduledTime": "2024-01-16T01:00:00Z",
  "priority": 1
}
```

### Health Check
```http
GET /health
```

## ⚙️ Configuration

### appsettings.json

```json
{
  "MongoDb": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "ApiTimeoutHandler",
    "CollectionName": "ApiData"
  },
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "TopicName": "api-timeout-events",
    "ClientId": "api-timeout-handler",
    "GroupId": "api-timeout-handler-group"
  },
  "ExternalApi": {
    "BaseUrl": "https://jsonplaceholder.typicode.com",
    "EndpointPath": "posts",
    "TimeoutSeconds": 30,
    "MaxRetries": 3,
    "RateLimitDelayMs": 1000
  }
}
```

## 🔄 How It Works

### 1. Daily Scheduled Calls (1 AM)
- Background service triggers at 1 AM daily
- Processes API IDs in batches with rate limiting
- Publishes Kafka events for scheduled execution
- Updates MongoDB cache with fresh data

### 2. Real-time API Requests
- Check MongoDB for cached data within 1-hour window
- Return cached data if fresh
- Call external API if cache miss or stale
- Update cache on successful API call
- Fall back to stale cache on API failure
- Schedule retry events for timeouts

### 3. Event Processing
- Kafka events trigger background API calls
- Exponential backoff for retry attempts
- Rate limiting to prevent API overload
- Comprehensive error handling and logging

## 🐳 Docker Deployment

### Full Stack Deployment
```bash
# Start everything including the API
docker-compose --profile app up -d
```

### Infrastructure Only
```bash
# Start only MongoDB and Kafka
docker-compose up -d mongodb kafka zookeeper
```

## 📊 Monitoring

### Kafka UI
- **URL**: `http://localhost:8080`
- Monitor topics, partitions, and consumer groups
- View message details and throughput

### MongoDB Express
- **URL**: `http://localhost:8081`
- Browse collections and documents
- Query and analyze cached data

### Application Logs
```bash
# View application logs
docker-compose logs -f api-timeout-handler

# View all service logs
docker-compose logs -f
```

## 🔧 Development

### Project Structure
```
ApiTimeoutHandler/
├── Controllers/           # API controllers
├── Models/               # Data models
├── Services/             # Business logic services
├── Program.cs           # Application entry point
├── appsettings.json     # Configuration
├── Dockerfile           # Container definition
└── docker-compose.yml   # Infrastructure setup
```

### Key Components

- **ApiDataController**: Main API endpoint with caching logic
- **ScheduledApiCallService**: Background service for daily processing
- **MongoRepository**: MongoDB data access layer
- **KafkaProducer**: Event publishing service
- **ExternalApiService**: HTTP client with timeout handling

## 🧪 Testing

### Manual Testing
```bash
# Test API endpoint
curl -X GET "https://localhost:5001/api/ApiData/1" -H "accept: application/json"

# Schedule API call
curl -X POST "https://localhost:5001/api/ApiData/schedule/1" \
  -H "Content-Type: application/json" \
  -d '{"scheduledTime": "2024-01-16T01:00:00Z", "priority": 1}'
```

### Health Check
```bash
curl -X GET "https://localhost:5001/health"
```

## 🚨 Error Handling

The system handles various error scenarios:

- **API Timeouts**: Falls back to cached data, schedules retries
- **Network Errors**: Exponential backoff retry logic
- **Cache Misses**: Graceful degradation with error responses
- **Service Failures**: Comprehensive logging and monitoring

## 📈 Performance Considerations

- **Connection Pooling**: HTTP client reuse
- **Database Indexing**: MongoDB indexes on apiId
- **Rate Limiting**: Configurable delays between API calls
- **Batch Processing**: Scheduled calls processed in batches
- **Memory Management**: Proper disposal of resources

## 🔒 Security

- **Input Validation**: Parameter validation in controllers
- **Error Sanitization**: Safe error message exposure
- **Connection Security**: Configurable connection strings
- **Container Security**: Non-root user in Docker containers

## 📝 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Commit your changes
4. Push to the branch
5. Create a Pull Request

## 📞 Support

For issues and questions:
- Create an issue in the repository
- Check the logs for detailed error information
- Review the configuration settings 
