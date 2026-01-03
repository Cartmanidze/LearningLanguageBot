# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Fixed
- Fixed JSONB serialization error for `List<TimeOnly>` (ReminderTimes) by enabling dynamic JSON on NpgsqlDataSource (Npgsql 8.0+ breaking change)