using Xunit;

// The fail-loud boot specs (FailLoudBootSpec) arm a globally-discovered throwing KoanModule and
// assert against the AppDomain-wide AppBootstrapper.RegistrySummary static. Running boot specs in
// parallel would let a concurrent AddKoan() overwrite that shared snapshot mid-assertion, so the whole
// reflective-discovery boot suite runs serially. These specs are cheap and few; serialization is safe.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
