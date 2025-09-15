param(
  [string]$Db = 's8',
  [string]$Mongo = 's8-mongo'
)

$collections = @(
  'Koan.Flow.Model.CanonicalProjectionView#canonical',
  'Koan.Flow.Model.KeyIndex',
  'Koan.Flow.Model.LineageProjectionView#lineage',
  'Koan.Flow.Model.PolicyBundle',
  'Koan.Flow.Model.ProjectionTask',
  'Koan.Flow.Model.Record#intake',
  'Koan.Flow.Model.Record#keyed',
  'Koan.Flow.Model.Record#standardized',
  'Koan.Flow.Model.ReferenceItem',
  'Koan.Flow.Model.RejectionReport',
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
