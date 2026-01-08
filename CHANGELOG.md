# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added
- "Don't Remember" button with memory hints for better word retention
  - Replaces "Skip" button in typing mode review
  - Shows etymology, usage context, simpler synonyms, and memory associations
  - All hints displayed in BOTH languages (native + target) for better understanding
  - Memory hints also shown on wrong answers, not just "Don't Remember"
  - Uses LLM to generate memorable hints for each word
  - Hints are cached in database for instant subsequent access
  - Visual association images from Unsplash API for better memorization

### Changed
- Reminders now send as long as there are due cards, regardless of daily goal completion
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
- `AnswerMatcher` service for comparing user answers with correct translations
- Custom reminder time selection during onboarding
  - Quick buttons: Morning (9:00), Day (14:00), Evening (20:00), All three
  - Text input: parse times like "9:00, 14:00, 20:00" or just "9 14 20"
- `/cards` command for browsing cards with pagination and search
- `/settings` command for changing language, reminder times, review mode, and timezone
- Timezone support for reminders (15 timezones including Russian cities, Europe, Asia, Americas)
- `/import` command for importing words from various sources
  - URL import: fetch articles from Medium, BBC, NYTimes, Wikipedia, etc.
  - Text import: paste any text and extract useful words
  - File import: upload .txt files
  - Song lyrics import with Genius API search by song name
  - LLM-based word extraction with context examples
  - Ability to review, remove, and add more words before creating cards

### Added
- Interactive `/help` command with quick action buttons (Learn, Cards, Import, Settings)
- Auto-registration of bot commands in Telegram menu on startup (visible when typing `/`)

### Improved
- Enhanced logging for word import feature
  - Log extraction requests: requested word count, text length, user level
  - Log extracted words: actual count and word list
  - Log card creation: source title, created count, duplicate count
  - Log user word removal during import review
  - Log JSON parsing failures with partial content

### Fixed
- Fixed review session not resetting card count on new day - `GetUserAsync` now automatically resets `TodayReviewed` when date changes
- Fixed duplicate reminder notifications - reduced time window from 1 minute to 30 seconds to prevent double sending at :59 and :00
- Fixed examples language - examples are now always in target language (the language user is learning), regardless of input language
- Fixed review mode not being respected - now ReviewHandler checks user's ReviewMode setting
- Fixed JSONB serialization error for `List<TimeOnly>` (ReminderTimes) by enabling dynamic JSON on NpgsqlDataSource (Npgsql 8.0+ breaking change)
- Fixed OpenRouter API URL - use full URL instead of relative path to avoid HttpClient BaseAddress path replacement issue
- Fixed card structure: Front = Russian (native), Back = English (target)
- Fixed examples to be generated only in target language