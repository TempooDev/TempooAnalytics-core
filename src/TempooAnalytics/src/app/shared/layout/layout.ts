import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet } from '@angular/router';
import { HeaderComponent } from '../components/header/header';
import { FooterComponent } from '../components/footer/footer';

@Component({
    selector: 'app-layout',
    standalone: true,
    imports: [CommonModule, RouterOutlet, HeaderComponent, FooterComponent],
    templateUrl: './layout.html',
})
export class LayoutComponent { }
