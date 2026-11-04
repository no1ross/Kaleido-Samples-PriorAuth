import { InformationRequest } from './information-request';

export interface ParticipantProcessResult {
    processId: string;
    state: string;
    requiredStep?: string;
    availableSteps: string[];
    steps: ParticipantStepResult[];
}

export interface ParticipantStepResult {
    stepName: string;
    response: any;
    executionStatus?: string;
    businessMessages: ProcessMessage[];
    frameworkMessages: ProcessMessage[];
}

export interface ProcessExecutionResponse<TResponse> {
    processId: string;
    stepName: string;
    /** What happened to the step; only 'completed' means it ran and succeeded. */
    status: StepExecutionStatus;
    result: TResponse;
    requiredStep?: ProcessRequiredStep;
    targetProcessorName?: string;
    availableSteps: ProcessStepSummary[];
    /** Set when the process continues with steps this caller may not run (pro_not_permitted). */
    notice?: KaleidoNotice;
    businessMessages: ProcessMessage[];
    frameworkMessages: ProcessMessage[];
}

/** Something the caller should know that is not an error, e.g. next steps it may not run. */
export interface KaleidoNotice {
    code: string;
    message: string;
}

export interface ProcessStepSummary {
    name: string;
    displayName?: string;
    description?: string;
    repeatable: boolean;
    isInformationStep?: boolean;
    executeUrl: string;
}

/** The step a process requires next; carries the pending questions for an information step. */
export interface ProcessRequiredStep extends ProcessStepSummary {
    informationRequest?: InformationRequest;
}

export interface ProcessMessage {
    type: string;
    message: string;
    code: string;
}

// Kaleido serializes enums as camelCase strings. 'pending': the step did not run (the framework
// messages say why, e.g. pro_process_ended); 'exception': a defect, retry later.
export type StepExecutionStatus =
    'pending'
    | 'completed'
    | 'rejected'
    | 'validationFailed'
    | 'failed'
    | 'exception'
    | 'canceled';
