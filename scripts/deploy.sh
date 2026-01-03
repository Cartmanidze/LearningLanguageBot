#!/bin/bash
# Manual deployment script for LearningLanguageBot
# Run from /opt/learninglanguagebot on server

set -e

echo "🚀 Deploying LearningLanguageBot..."

cd /opt/learninglanguagebot

# Check .env exists
if [ ! -f .env ]; then
    echo "❌ No .env file found. Please configure .env first."
    exit 1
fi

# Load environment
source .env

# Check required variables
if [ -z "$TELEGRAM_BOT_TOKEN" ]; then
    echo "❌ TELEGRAM_BOT_TOKEN is not set in .env"
    exit 1
fi

if [ -z "$OPENROUTER_API_KEY" ]; then
    echo "❌ OPENROUTER_API_KEY is not set in .env"
    exit 1
fi

if [ -z "$POSTGRES_PASSWORD" ]; then
    echo "❌ POSTGRES_PASSWORD is not set in .env"
    exit 1
fi

# Pull latest image
echo "📦 Pulling latest image..."
docker pull ${DOCKER_IMAGE:-ghcr.io/cartmanidze/learninglanguagebot:latest}

# Restart services
echo "🔄 Restarting services..."
docker compose -f docker-compose.server.yml down
docker compose -f docker-compose.server.yml up -d

# Wait for services
echo "⏳ Waiting for services to start..."
sleep 10

# Health check
echo "🔍 Checking services..."
if docker ps | grep -q learning-language-bot; then
    echo "✅ Bot is running"
else
    echo "❌ Bot failed to start"
    docker logs learning-language-bot --tail 50
    exit 1
fi

if docker ps | grep -q learning-language-bot-db; then
    echo "✅ Database is running"
else
    echo "❌ Database failed to start"
    exit 1
fi

# Cleanup
echo "🧹 Cleaning up old images..."
docker image prune -f

echo ""
echo "✅ Deployment complete!"
echo "📊 View logs: docker logs -f learning-language-bot"
