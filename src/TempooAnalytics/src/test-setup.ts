import '@testing-library/jest-dom';

// Ensure JIT compiler is available for libraries that require JIT fallback.
import '@angular/compiler';

// Initialize Angular TestBed environment so TestBed-based tests work under Vitest.
import { TestBed } from '@angular/core/testing';
import { BrowserDynamicTestingModule, platformBrowserDynamicTesting } from '@angular/platform-browser-dynamic/testing';

TestBed.initTestEnvironment(BrowserDynamicTestingModule, platformBrowserDynamicTesting());
