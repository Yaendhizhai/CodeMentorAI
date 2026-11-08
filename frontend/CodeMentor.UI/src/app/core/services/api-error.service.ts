import { Injectable } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiProblemDetails } from '../models/repository-project.model';

@Injectable({ providedIn: 'root' })
export class ApiErrorService {
  message(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) {
        return 'The API could not be reached. Check that the backend is running at http://localhost:5269.';
      }

      const problem = this.problemDetails(error.error);
      return problem?.detail ?? problem?.title ?? `The request failed with status ${error.status}.`;
    }

    return 'An unexpected error occurred. Please try again.';
  }

  validationMessages(error: unknown): Readonly<Record<string, readonly string[]>> {
    if (!(error instanceof HttpErrorResponse)) {
      return {};
    }

    return this.problemDetails(error.error)?.errors ?? {};
  }

  private problemDetails(value: unknown): ApiProblemDetails | null {
    if (typeof value !== 'object' || value === null) {
      return null;
    }

    return value as ApiProblemDetails;
  }
}
