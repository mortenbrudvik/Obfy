import { configure } from "@testing-library/react";

// findBy* and waitFor give up after one second by default, and a test after five. The screens these tests drive fill in
// from several asynchronous reads (files, run, settings, results), which a busy machine or a small CI runner can
// stretch past that. A test that finds its element finds it at once, so longer limits change only the tests that would
// otherwise fail by timing, not what any of them asserts.
configure({ asyncUtilTimeout: 5000 });
