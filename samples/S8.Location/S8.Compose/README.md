# S8.Location Complete Stack

Full Docker Compose deployment for the S8.Location canonical location standardization system.

## 🚀 Quick Start

```bash
# Start all services
docker-compose up

# Or start in background
docker-compose up -d
```

## 🌐 Access Points

- **📊 Dashboard**: [`http://localhost:4915`](http://localhost:4915) - Vue.js UI
- **🔌 API**: [`http://localhost:4914`](http://localhost:4914) - S8.Location API
- **📋 API Docs**: [`http://localhost:4914/swagger`](http://localhost:4914/swagger) - Swagger UI
- **🐰 RabbitMQ**: [`http://localhost:4912`](http://localhost:4912) - Management UI (guest/guest)
- **🍃 MongoDB**: `localhost:4910` - Database connection

## 🐳 Services

| Service | Container | Port | Description |
|---------|-----------|------|-------------|
| **UI** | `s8-location-ui` | 4915 | Vue.js Dashboard |
| **API** | `s8-location-api` | 4914 | Location API |
| **MongoDB** | `s8-location-mongo` | 4910 | Database |
| **RabbitMQ** | `s8-location-rabbitmq` | 4911, 4912 | Message Queue |
| **Inventory Adapter** | `s8-location-adapter-inventory` | - | Data Source |
| **Healthcare Adapter** | `s8-location-adapter-healthcare` | - | Data Source |

## 🔧 Configuration

### Environment Variables
```bash
# Optional: Google Maps API for geocoding
export GOOGLE_MAPS_API_KEY="your-api-key"

# Ollama AI must be running on host
# Default: http://localhost:11434
```

### AI Requirements
- **Ollama** must be running on the host at `localhost:11434`
- The system uses auto-discovery to connect to Ollama
- Any model compatible with Ollama will work

## 📊 Features

- **Real-time location processing** with AI standardization
- **Canonical deduplication** using SHA512 hashing
- **Flow orchestration** (Park → Resolve → Imprint → Promote)
- **Multiple data sources** (inventory, healthcare adapters)
- **Professional dashboard** with live monitoring
- **Health checks** and monitoring across all services

## 🛠️ Commands

```bash
# Build all services
docker-compose build

# Start specific service
docker-compose up api

# View logs
docker-compose logs -f api

# Stop all services  
docker-compose down

# Reset everything
docker-compose down -v && docker-compose build --no-cache
```

## 🔍 Monitoring

Check service status:
```bash
# View all containers
docker-compose ps

# Check health
curl http://localhost:4914/health

# View API logs
docker-compose logs api
```

## 🎯 Usage

1. **Start the stack**: `docker-compose up`
2. **Open dashboard**: [`http://localhost:4915`](http://localhost:4915)
3. **Submit locations** via API or adapters
4. **Watch processing** in real-time
5. **Test AI corrections** in the dashboard

The system will automatically:
- ✅ Process incoming locations
- ✅ Apply AI address standardization  
- ✅ Create canonical location IDs
- ✅ Deduplicate across sources
- ✅ Update the dashboard in real-time

---

**Built with ❤️ for the Sora Framework ecosystem**