# S8 Location Dashboard - Real-time Location Processing UI

A modern, professional Vue 3 dashboard for the S8.Location canonical location standardization system.

## ✨ Features

### 🚀 **Real-time Location Stream**
- Live feed of incoming locations being processed
- Visual processing pipeline (Park → Resolve → Imprint → Promote)
- AI correction highlighting with confidence scores
- Real-time statistics and performance metrics

### 🧠 **AI Address Tester** 
- Interactive address testing tool
- Real-time AI correction preview
- Diff viewer showing exact changes
- Canonical ID generation insights

### 📊 **System Health Monitoring**
- Real-time service status (MongoDB, RabbitMQ, Ollama AI)
- Performance metrics and response times
- Quick diagnostic actions

## 🛠️ Technology Stack

- **Vue 3 + TypeScript** - Modern reactive framework
- **Vite** - Fast build tool and dev server  
- **Tailwind CSS** - Professional styling
- **Axios** - API communication

## 🚀 Quick Start

### **Option 1: Full Docker Stack (Recommended)**
```bash
# From the S8.Compose directory
cd samples/S8.Location/S8.Compose
docker-compose up
```

Visit `http://localhost:4915` for the dashboard.
All services (API, UI, MongoDB, RabbitMQ) start together.

### **Option 2: Development Mode**
```bash
# Install dependencies
npm install

# Start development server  
npm run dev
```

Visit `http://localhost:5173` to view the dashboard.

**Prerequisites**: S8.Location API running on `localhost:4914`

## 🎨 Dashboard Highlights

- **Professional Design** - Clean, modern interface with great UX
- **Real-time Updates** - Live location processing stream
- **AI Visualization** - See address corrections in action
- **Performance Monitoring** - System health and metrics
- **Responsive Design** - Works on desktop and mobile

Built with ❤️ for the Koan Framework ecosystem
