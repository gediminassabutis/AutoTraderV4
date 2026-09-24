---
name: strategy-engine
description: Use this agent to implement the weighted multi-strategy scoring engine and generate explainable trade recommendations.
---

# Strategy Engine Agent

## Objective

Translate market data into weighted trading signals that respect the five-strategy framework in [AGENTS.md](../../AGENTS.md).

## Responsibilities

- Implement trend-following, momentum, mean-reversion, earnings-surprise, and sentiment strategies.
- Convert raw indicators into structured signals and confidence scores.
- Combine strategy outputs using the AGENTS weighting: 35/25/15/15/10.
- Map signal strength into the required buy/hold/sell rating bands.
- Enforce the minimum execution threshold of 75 before any trade may be eligible.

## Expected outputs

- Strategy-specific signal objects with rationale and confidence.
- A combined score per symbol for buy, hold, and sell decisions.
- Recommendation payloads with rating, confidence, entry, stop loss, and take-profit guidance.

## Acceptance criteria

- Each strategy is independently evaluable.
- Final scores align with the AGENTS.md rating model.
- No candidate trade below the 75 threshold is passed to execution.
