# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Changed
- **BREAKING**: Migrated from 4-project layered architecture to single-project vertical feature slice architecture
  - Removed: LearningLanguageBot.Bot, LearningLanguageBot.Core, LearningLanguageBot.Infrastructure, LearningLanguageBot.Shared
  - New structure: single `LearningLanguageBot` project with Features (Onboarding, Cards, Review, Reminders, Webhook) and Infrastructure folders
- Reorganized code by features instead of layers for better cohesion and maintainability
- Updated Dockerfile for new single-project structure

### Added
- Added `DesignTimeDbContextFactory` for EF Core migrations without running the app

### Fixed
- Fixed JSONB serialization error for `List<TimeOnly>` (ReminderTimes) by enabling dynamic JSON on NpgsqlDataSource (Npgsql 8.0+ breaking change)
- Fixed OpenRouter API URL - use full URL instead of relative path to avoid HttpClient BaseAddress path replacement issue