import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  RegisterRepositoryProjectRequest,
  RepositoryIngestionResponse,
  RepositoryProject,
} from '../models/repository-project.model';

@Injectable({ providedIn: 'root' })
export class RepositoryApiService {
  private readonly http = inject(HttpClient);
  private readonly projectsUrl = `${environment.apiBaseUrl}/repository-projects`;

  register(request: RegisterRepositoryProjectRequest): Observable<RepositoryProject> {
    return this.http.post<RepositoryProject>(this.projectsUrl, request);
  }

  getAll(): Observable<RepositoryProject[]> {
    return this.http.get<RepositoryProject[]>(this.projectsUrl);
  }

  getById(id: string): Observable<RepositoryProject> {
    return this.http.get<RepositoryProject>(`${this.projectsUrl}/${encodeURIComponent(id)}`);
  }

  ingest(id: string): Observable<RepositoryIngestionResponse> {
    return this.http.post<RepositoryIngestionResponse>(
      `${this.projectsUrl}/${encodeURIComponent(id)}/ingest`,
      null,
    );
  }
}
