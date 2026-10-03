// A fresh instance per test, as under xUnit: the tests were written against constructor state,
// per-test mocks and a Dispose that runs after every test, not once per fixture.
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
