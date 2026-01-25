# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Changed
- **BREAKING**: Replaced SM-2 algorithm with FSRS (Free Spaced Repetition Scheduler)
  - FSRS is 20-30% more efficient at the same retention level
  - 4 rating options instead of binary (Again/Hard/Good/Easy)
  - Review buttons now show predicted intervals (e.g., "👍 Хорошо (1д)")
  - Automatic rating in Typing mode based on response time and accuracy
  - Database migration automatically converts existing SM-2 cards to FSRS format
  - Uses FSRS.Core NuGet package with dependency injection support

### Fixed
- Fixed Telegram callback timeout error when generating memory hints
  - "Don't Remember" button now immediately answers callback before LLM generation
  - Prevents "query is too old and response timeout expired" errors
- Fixed FSRS crash on new cards with null LastReview
  - "Nullable object must have a value" error when reviewing new cards
  - Now defaults to current time for cards without LastReview
- Fixed FSRS crash on Relearning cards with missing Step
  - "Nullable object must have a value" in `HandleGoodRating` when reviewing cards in Relearning state
  - Added `Step` field to Card model to properly track Learning/Relearning progress
  - Migration: `AddCardStep` adds nullable `Step` column to Cards table

### Improved
- Optimized memory hint generation speed
  - Reduced MaxTokens from 1000 to 200 for faster LLM response
  - Simplified prompt for more concise hints (3-4 lines instead of detailed format)
  - Added detailed timing logs (headers vs total time) for debugging

### Changed
- Improved memory hints with phonetic anchor technique
  - New 3-section format: "Sounds like" → "Imagine" → "Remember"
  - Phonetic associations based on similar sounds in native language
  - Vivid, absurd, or emotional imagery for better retention
  - Example: "serendipity" ≈ "СЭР + ИНДИЯ + ТИПА" → "Сэр в Индии типа нашёл клад"

### Removed
- Unsplash image integration (removed UnsplashService)
  - Images were not effective for memorization
  - Text-based phonetic associations work better

### Added
- "Don't Remember" button with memory hints for better word retention
  - Replaces "Skip" button in typing mode review
  - Memory hints also shown on wrong answers, not just "Don't Remember"
  - Uses LLM to generate memorable hints for each word
  - Hints are cached in database for instant subsequent access

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