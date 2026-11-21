import { configure } from "@testing-library/react";

// findBy* and waitFor give up after one second by default; here they wait five. A test as a whole gives up after fifteen
// (testTimeout in vite.config.ts), so a wait that runs out still ends before the test does and says what it waited for.
// The screens these tests drive fill in from several asynchronous reads (files, run, settings, results), which a busy
// machine or a small CI runner can stretch past one second. A test that finds its element finds it at once, so longer
// limits change only the tests that would otherwise fail by timing, not what any of them asserts.
configure({ asyncUtilTimeout: 5000 });
