import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ApiErrorService } from '../../core/services/api-error.service';
import { RepositoryApiService } from '../../core/services/repository-api.service';
import { CodeChunkSearchResult } from '../../core/models/code-search.model';
import { RepositoryProject } from '../../core/models/repository-project.model';
import { PageHeadingComponent } from '../../shared/components/page-heading.component';

const searchTermsPattern = /[\p{L}\p{N}]+/gu;

@Component({
  imports: [PageHeadingComponent, ReactiveFormsModule],
  templateUrl: './code-explorer.component.html',
  styleUrl: './code-explorer.component.scss',
})
export class CodeExplorerComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly repositoryApi = inject(RepositoryApiService);
  private readonly apiErrors = inject(ApiErrorService);
  private readonly destroyRef = inject(DestroyRef);
  private searchRequestVersion = 0;

  protected readonly repositories = signal<readonly RepositoryProject[]>([]);
  protected readonly repositoriesLoading = signal(true);
  protected readonly repositoriesError = signal('');
  protected readonly searchLoading = signal(false);
  protected readonly searchError = signal('');
  protected readonly validationError = signal('');
  protected readonly hasSearched = signal(false);
  protected readonly results = signal<readonly CodeChunkSearchResult[]>([]);

  protected readonly form = this.formBuilder.nonNullable.group({
    repositoryId: ['', Validators.required],
    query: ['', Validators.maxLength(200)],
    limit: [10, [Validators.min(1), Validators.max(100)]],
  });

  constructor() {
    this.loadRepositories();
  }

  protected loadRepositories(): void {
    this.repositoriesLoading.set(true);
    this.repositoriesError.set('');
    this.repositoryApi
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (repositories) => {
          this.repositories.set(repositories);
          this.repositoriesLoading.set(false);
        },
        error: (error: unknown) => {
          this.repositoriesError.set(this.apiErrors.message(error));
          this.repositoriesLoading.set(false);
        },
      });
  }

  protected repositoryChanged(): void {
    this.searchRequestVersion++;
    this.searchLoading.set(false);
    this.searchError.set('');
    this.validationError.set('');
    this.hasSearched.set(false);
    this.results.set([]);
  }

  protected search(): void {
    const repositoryId = this.form.controls.repositoryId.value;
    if (!repositoryId) {
      this.validationError.set('Select a repository before searching.');
      return;
    }

    const query = this.form.controls.query.value;
    const validationError = this.validateQuery(query);
    if (validationError) {
      this.validationError.set(validationError);
      return;
    }

    this.validationError.set('');
    this.searchError.set('');
    this.hasSearched.set(true);
    this.results.set([]);
    this.searchLoading.set(true);
    const requestVersion = ++this.searchRequestVersion;
    const limit = this.form.controls.limit.value;

    this.repositoryApi
      .searchCode(repositoryId, { query, limit })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (results) => {
          if (this.isCurrentRequest(requestVersion, repositoryId)) {
            this.results.set(results);
            this.searchLoading.set(false);
          }
        },
        error: (error: unknown) => {
          if (this.isCurrentRequest(requestVersion, repositoryId)) {
            this.searchError.set(this.apiErrors.message(error));
            this.searchLoading.set(false);
          }
        },
      });
  }

  protected queryChanged(): void {
    this.validationError.set('');
  }

  private isCurrentRequest(requestVersion: number, repositoryId: string): boolean {
    return requestVersion === this.searchRequestVersion &&
      repositoryId === this.form.controls.repositoryId.value;
  }

  private validateQuery(query: string): string {
    if (!query.trim()) {
      return 'Enter a search query.';
    }

    if (query.length > 200) {
      return 'Search queries must be 200 characters or fewer.';
    }

    const terms = query.normalize('NFKC').match(searchTermsPattern) ?? [];
    const distinctTerms = new Set(terms.map((term) => term.toLowerCase()));
    if (distinctTerms.size === 0) {
      return 'Enter a query containing at least one letter or number.';
    }

    if (distinctTerms.size > 10) {
      return 'Use no more than 10 distinct search terms.';
    }

    return '';
  }
}
