# Changelog

All notable changes to wc3ctl. One file per release lives under
[`changelog/`](changelog/), and this file shows the latest one. The format
follows Keep a Changelog, and the project follows Conventional Commits.

## 0.4.11 - 2026-10-21

The lossy model run, with a receipt.

### Added

- `model squish` and `model_squish`, a render gate on every lossy step. It runs the lossless
  key drop, then quadric decimation, renders the original and the candidate from four angles
  after each pass, and keeps a pass only while mean SSIM and the worst silhouette overlap stay
  above the floor. The receipt gives the bytes and the score for every pass, kept or refused.
  Optionally it shrinks the model's own textures into a side folder, never the model in place.
