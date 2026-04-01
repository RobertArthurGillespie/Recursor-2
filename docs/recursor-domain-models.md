# Recursor Engine Domain Model Spec

## Core model layers

1. Raw telemetry
2. Derived feature windows
3. Behavior profiles
4. Hypothesis sets
5. Adaptation decisions

## Raw telemetry

### RawEventBatch
Fields:
- SchemaVersion
- BatchId
- SessionId
- UserId
- SimId
- SimVersion
- ScenarioId
- ClientTimestampUtc
- BatchSequence
- Events

### RawEventRecord
Fields:
- EventId
- SequenceNumber
- TimestampUtc
- EventType
- Category
- Actor
- Target
- Context
- Metrics
- Payload

### EventMetrics
Fields:
- Value
- DurationMs
- Distance
- Score
- AdditionalMetrics

## Session model

### SessionDocument
Fields:
- Id
- DocumentType
- SessionId
- UserId
- SimId
- SimVersion
- ScenarioId
- Status
- StartedAtUtc
- LastSeenAtUtc
- EventCount
- BatchCount
- CurrentStage
- CurrentDifficultyProfile
- LatestFeatureWindowId
- LatestBehaviorProfileId
- LatestHypothesisSetId
- LatestAdaptationId
- Summary

### SessionSummary
Fields:
- CompletionPercent
- ErrorCount
- HintCount
- SafetyViolationCount

## Feature model

### FeatureWindowDocument
Fields:
- Id
- DocumentType
- SessionId
- WindowIndex
- WindowType
- WindowStartSequence
- WindowEndSequence
- WindowStartUtc
- WindowEndUtc
- SimId
- ScenarioId
- Features

### BehavioralFeatureSet
Contains:
- AttentionDetection
- GoalUnderstanding
- ProcedureSequencing
- PaceRegulation
- SelfCorrection
- FeedbackResponsiveness
- SafetyCompliance
- TaskContinuity

## Interpretation model

### BehaviorProfileDocument
Fields:
- Id
- DocumentType
- SessionId
- WindowIndex
- SourceFeatureWindowId
- DimensionScores

### DimensionScore
Fields:
- Score
- Confidence
- Evidence

### HypothesisSetDocument
Fields:
- Id
- DocumentType
- SessionId
- WindowIndex
- SourceBehaviorProfileId
- Hypotheses
- InterpreterMode
- InterpreterVersion

### BehavioralHypothesis
Fields:
- Label
- Dimensions
- Confidence
- Evidence

## Adaptation model

### AdaptationDecisionDocument
Fields:
- Id
- DocumentType
- SessionId
- DecisionIndex
- SourceHypothesisSetId
- InterventionFamilies
- ParameterChanges
- ReasoningSummary
- ExpiresAfterWindow

### ParameterChange
Fields:
- Parameter
- Operation
- Value

## Sim catalog model

### SimCatalogDocument
Fields:
- Id
- DocumentType
- SimId
- SupportedDimensions
- EventTypes
- AdaptiveParameters

### AdaptiveParameterDefinition
Fields:
- Name
- Type
- Min
- Max
- AllowedValues

## Naming guidance

Keep these names stable if implemented:
- attentionDetection
- goalUnderstanding
- procedureSequencing
- paceRegulation
- selfCorrection
- feedbackResponsiveness
- safetyCompliance
- taskContinuity

## Coding guidance

- For this stage, prefer string constants over enums for document types, dimension names, and operations.
- Prefer explicit records/classes with readable property names.
- Do not compress multiple concepts into one mega-document.