param(
  [string]$Db = 's8',
  [string]$Mongo = 's8-mongo'
)

$collections = @(
  'Sora.Flow.Model.CanonicalProjectionView#canonical',
  'Sora.Flow.Model.KeyIndex',
  'Sora.Flow.Model.LineageProjectionView#lineage',
  'Sora.Flow.Model.PolicyBundle',
  'Sora.Flow.Model.ProjectionTask',
  'Sora.Flow.Model.Record#intake',
  'Sora.Flow.Model.Record#keyed',
  'Sora.Flow.Model.Record#standardized',
  'Sora.Flow.Model.ReferenceItem',
  'Sora.Flow.Model.RejectionReport',
  'S8.Flow.Shared.Device#flow.device.intake'
)

foreach ($c in $collections) {
  $eval = "if (db.getCollectionNames().includes('$c')) db.getCollection('$c').drop()"
  Write-Host "Dropping $c if exists..."
  docker exec $Mongo mongosh $Db --quiet --eval $eval | Out-Null
}

Write-Host "Remaining collections:" -ForegroundColor Cyan
$remaining = docker exec $Mongo mongosh $Db --quiet --eval 'JSON.stringify(db.getCollectionNames().sort())'
Write-Output $remaining
