#!/bin/bash
# Server initial setup script for LearningLanguageBot
# Run on fresh Ubuntu/Debian server

set -e

echo "🚀 Setting up LearningLanguageBot server..."

# Update system
echo "📦 Updating system packages..."
apt-get update && apt-get upgrade -y

# Install Docker if not present
if ! command -v docker &> /dev/null; then
    echo "🐳 Installing Docker..."
    curl -fsSL https://get.docker.com | sh
    systemctl enable docker
    systemctl start docker
fi

# Install Docker Compose plugin if not present
if ! docker compose version &> /dev/null; then
    echo "🐳 Installing Docker Compose..."
    apt-get install -y docker-compose-plugin
fi

# Create application directory
echo "📁 Creating application directory..."
mkdir -p /opt/learninglanguagebot
cd /opt/learninglanguagebot

# Create .env file template
if [ ! -f .env ]; then
    echo "📝 Creating .env template..."
    cat > .env << 'EOF'
# Telegram Bot
TELEGRAM_BOT_TOKEN=

# OpenRouter (for DeepSeek LLM)
OPENROUTER_API_KEY=
OPENROUTER_MODEL=deepseek/deepseek-v3.2

# Database
POSTGRES_PASSWORD=

# Docker image
DOCKER_IMAGE=ghcr.io/cartmanidze/learninglanguagebot:latest
EOF
    echo "⚠️  Please edit /opt/learninglanguagebot/.env with your credentials"
fi

# Create docker-compose.server.yml
echo "📝 Creating docker-compose.server.yml..."
cat > docker-compose.server.yml << 'EOF'
# Production configuration - pulls pre-built image from GHCR

services:
  postgres:
    image: postgres:16-alpine
    container_name: learning-language-bot-db
    environment:
      POSTGRES_DB: learninglanguagebot
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d learninglanguagebot"]
      interval: 10s
      timeout: 5s
      retries: 5
    restart: unless-stopped

  bot:
    image: ${DOCKER_IMAGE:-ghcr.io/cartmanidze/learninglanguagebot:latest}
    container_name: learning-language-bot
    environment:
      ConnectionStrings__DefaultConnection: "Host=postgres;Database=learninglanguagebot;Username=postgres;Password=${POSTGRES_PASSWORD}"
      Telegram__BotToken: ${TELEGRAM_BOT_TOKEN}
      OpenRouter__ApiKey: ${OPENROUTER_API_KEY}
      OpenRouter__Model: ${OPENROUTER_MODEL:-deepseek/deepseek-v3.2}
    depends_on:
      postgres:
        condition: service_healthy
    restart: unless-stopped

volumes:
  postgres_data:
EOF

echo ""
echo "✅ Server setup complete!"
echo ""
echo "Next steps:"
echo "1. Edit /opt/learninglanguagebot/.env with your credentials"
echo "2. Login to GitHub Container Registry:"
echo "   echo YOUR_GITHUB_TOKEN | docker login ghcr.io -u YOUR_USERNAME --password-stdin"
echo "3. Start the bot:"
echo "   cd /opt/learninglanguagebot && docker compose -f docker-compose.server.yml up -d"
echo ""
