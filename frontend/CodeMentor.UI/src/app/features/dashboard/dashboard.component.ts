import { Component, DestroyRef, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PageHeadingComponent } from '../../shared/components/page-heading.component';
import { FeatureUnavailableComponent } from '../../shared/components/feature-unavailable.component';
import { RepositoryApiService } from '../../core/services/repository-api.service';
import { ApiErrorService } from '../../core/services/api-error.service';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

@Component({
  imports: [RouterLink, PageHeadingComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent {
  private readonly repositoryApi = inject(RepositoryApiService);
  private readonly apiErrors = inject(ApiErrorService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly repositoryCount = signal<number | null>(null);
  protected readonly repositoriesLoading = signal(true);
  protected readonly repositoriesError = signal('');

  constructor() {
    this.repositoryApi
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (projects) => {
          this.repositoryCount.set(projects.length);
          this.repositoriesLoading.set(false);
        },
        error: (error: unknown) => {
          this.repositoriesError.set(this.apiErrors.message(error));
          this.repositoriesLoading.set(false);
        },
      });
  }
}
