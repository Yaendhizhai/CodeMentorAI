import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PageHeadingComponent } from '../../shared/components/page-heading.component';
import { FeatureUnavailableComponent } from '../../shared/components/feature-unavailable.component';

@Component({
  imports: [RouterLink, PageHeadingComponent, FeatureUnavailableComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent {}
