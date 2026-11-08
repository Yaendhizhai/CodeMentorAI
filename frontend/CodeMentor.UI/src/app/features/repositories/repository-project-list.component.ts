import { DatePipe } from '@angular/common';
import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RepositoryProject } from '../../core/models/repository-project.model';

@Component({
  imports: [DatePipe, RouterLink],
  selector: 'app-repository-project-list',
  templateUrl: './repository-project-list.component.html',
  styleUrl: './repository-project-list.component.scss',
})
export class RepositoryProjectListComponent {
  readonly projects = input<readonly RepositoryProject[]>([]);
  readonly loading = input(false);
  readonly error = input('');
  readonly refresh = output<void>();
}
