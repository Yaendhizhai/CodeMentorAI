import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RepositoryApiService } from '../../core/services/repository-api.service';
import { ApiErrorService } from '../../core/services/api-error.service';
import { RepositoryProject } from '../../core/models/repository-project.model';
import { PageHeadingComponent } from '../../shared/components/page-heading.component';
import { FeatureUnavailableComponent } from '../../shared/components/feature-unavailable.component';

@Component({
  imports: [DatePipe, RouterLink, PageHeadingComponent, FeatureUnavailableComponent],
  templateUrl: './repository-details.component.html',
  styleUrl: './repository-details.component.scss',
})
export class RepositoryDetailsComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly repositoryApi = inject(RepositoryApiService);
  private readonly apiErrors = inject(ApiErrorService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly project = signal<RepositoryProject | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const id = params.get('id');
      if (!id) {
        this.error.set('A repository project ID is required.');
        this.loading.set(false);
        return;
      }

      this.loading.set(true);
      this.error.set('');
      this.repositoryApi
        .getById(id)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (project) => {
            this.project.set(project);
            this.loading.set(false);
          },
          error: (requestError: unknown) => {
            this.error.set(this.apiErrors.message(requestError));
            this.loading.set(false);
          },
        });
    });
  }
}
