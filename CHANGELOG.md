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
- Implemented Typing review mode (F5) - user types translation instead of just revealing it
  - Fuzzy matching with Levenshtein distance for answer comparison
  - Three match results: Exact (auto-accept), Partial (user chooses), Wrong (auto-reject)
  - Skip button to skip difficult cards
- `AnswerMatcher` service for comparing user answers with correct translations
- Custom reminder time selection during onboarding
  - Quick buttons: Morning (9:00), Day (14:00), Evening (20:00), All three
  - Text input: parse times like "9:00, 14:00, 20:00" or just "9 14 20"

### Fixed
- Fixed review mode not being respected - now ReviewHandler checks user's ReviewMode setting
- Fixed JSONB serialization error for `List<TimeOnly>` (ReminderTimes) by enabling dynamic JSON on NpgsqlDataSource (Npgsql 8.0+ breaking change)
- Fixed OpenRouter API URL - use full URL instead of relative path to avoid HttpClient BaseAddress path replacement issue