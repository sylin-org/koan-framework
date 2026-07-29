[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SourceRoot,

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [string] $SourceCommit = "unknown",

    [string] $PrimerPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RelativePath {
    param([string] $Root, [string] $Path)
    return [System.IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
}

function Has-Modifier {
    param($Node, [string] $Modifier)
    foreach ($token in $Node.Modifiers) {
        if ($token.ValueText -eq $Modifier) { return $true }
    }
    return $false
}

function Get-LineNumber {
    param($Tree, $Node)
    return $Tree.GetLineSpan($Node.Span).StartLinePosition.Line + 1
}

function Get-NamespaceName {
    param($Node)
    $parts = [System.Collections.Generic.List[string]]::new()
    $current = $Node.Parent
    while ($null -ne $current) {
        if ($current -is [Microsoft.CodeAnalysis.CSharp.Syntax.BaseNamespaceDeclarationSyntax]) {
            $parts.Insert(0, $current.Name.ToString())
        }
        $current = $current.Parent
    }
    return ($parts -join '.')
}

function Get-TypeName {
    param($Node)
    $parts = [System.Collections.Generic.List[string]]::new()
    $current = $Node
    while ($null -ne $current) {
        if ($current -is [Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax] -or
            $current -is [Microsoft.CodeAnalysis.CSharp.Syntax.DelegateDeclarationSyntax]) {
            $name = $current.Identifier.ValueText
            $typeParameters = $current.PSObject.Properties['TypeParameterList']
            if ($null -ne $typeParameters -and $null -ne $typeParameters.Value) {
                $name += "``$($typeParameters.Value.Parameters.Count)"
            }
            $parts.Insert(0, $name)
        }
        $current = $current.Parent
    }
    $ns = Get-NamespaceName $Node
    if ([string]::IsNullOrWhiteSpace($ns)) { return ($parts -join '+') }
    return "$ns.$($parts -join '+')"
}

function Test-PublicType {
    param($Node)
    $current = $Node
    while ($null -ne $current) {
        if ($current -is [Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax] -or
            $current -is [Microsoft.CodeAnalysis.CSharp.Syntax.DelegateDeclarationSyntax]) {
            $parentIsInterface = $current.Parent -is [Microsoft.CodeAnalysis.CSharp.Syntax.InterfaceDeclarationSyntax]
            if (-not (Has-Modifier $current 'public') -and -not $parentIsInterface) { return $false }
        }
        $current = $current.Parent
    }
    return $true
}

function Test-PublicMember {
    param($Node)
    if ($Node.Parent -is [Microsoft.CodeAnalysis.CSharp.Syntax.InterfaceDeclarationSyntax]) { return $true }
    if ($Node.Parent -is [Microsoft.CodeAnalysis.CSharp.Syntax.EnumDeclarationSyntax]) { return $true }
    return Has-Modifier $Node 'public'
}

function Get-ProjectRole {
    param([string] $RelativeProject)
    if ($RelativeProject.StartsWith('src/Connectors/Data/', [System.StringComparison]::OrdinalIgnoreCase)) {
        return 'Adapter'
    }
    $name = [System.IO.Path]::GetFileNameWithoutExtension($RelativeProject)
    if ($name -in @('Koan.Data.Abstractions', 'Koan.Data.Core')) { return 'Framework' }
    if ($name -in @('Koan.Data.Relational', 'Koan.Data.Relational.Abstractions', 'Koan.Data.Relational.Npgsql',
                    'Koan.Data.Vector', 'Koan.Data.Vector.Abstractions')) { return 'Family' }
    if ($name -in @('Koan.Data.Backup', 'Koan.Data.SoftDelete')) { return 'Extension' }
    if ($name -eq 'Koan.Data.AI') { return 'Adjacent' }
    return 'Unclassified'
}

function Get-TypeKind {
    param($Node)
    switch ($Node.GetType().Name) {
        'ClassDeclarationSyntax' { return 'class' }
        'StructDeclarationSyntax' { return 'struct' }
        'InterfaceDeclarationSyntax' { return 'interface' }
        'RecordDeclarationSyntax' { return 'record' }
        'EnumDeclarationSyntax' { return 'enum' }
        'DelegateDeclarationSyntax' { return 'delegate' }
        default { return 'type' }
    }
}

function Get-MemberKind {
    param($Node)
    switch ($Node.GetType().Name) {
        'MethodDeclarationSyntax' { return 'method' }
        'ConstructorDeclarationSyntax' { return 'constructor' }
        'DestructorDeclarationSyntax' { return 'destructor' }
        'PropertyDeclarationSyntax' { return 'property' }
        'IndexerDeclarationSyntax' { return 'indexer' }
        'EventDeclarationSyntax' { return 'event' }
        'EventFieldDeclarationSyntax' { return 'event-field' }
        'FieldDeclarationSyntax' { return 'field' }
        'OperatorDeclarationSyntax' { return 'operator' }
        'ConversionOperatorDeclarationSyntax' { return 'conversion' }
        'EnumMemberDeclarationSyntax' { return 'enum-member' }
        default { return 'member' }
    }
}

function Get-MemberNames {
    param($Node)
    if ($Node -is [Microsoft.CodeAnalysis.CSharp.Syntax.BaseFieldDeclarationSyntax]) {
        return @($Node.Declaration.Variables | ForEach-Object { $_.Identifier.ValueText })
    }
    if ($Node -is [Microsoft.CodeAnalysis.CSharp.Syntax.IndexerDeclarationSyntax]) { return @('this[]') }
    if ($Node -is [Microsoft.CodeAnalysis.CSharp.Syntax.OperatorDeclarationSyntax]) { return @("operator $($Node.OperatorToken.ValueText)") }
    if ($Node -is [Microsoft.CodeAnalysis.CSharp.Syntax.ConversionOperatorDeclarationSyntax]) { return @("$($Node.ImplicitOrExplicitKeyword.ValueText) operator") }
    $identifier = $Node.PSObject.Properties['Identifier']
    if ($null -ne $identifier -and $null -ne $identifier.Value) { return @($identifier.Value.ValueText) }
    return @($Node.GetType().Name)
}

function Get-ParameterSignature {
    param($Node)
    $parameterList = $Node.PSObject.Properties['ParameterList']
    if ($null -eq $parameterList -or $null -eq $parameterList.Value) { return '' }
    return ($parameterList.Value.Parameters | ForEach-Object {
        $modifier = ($_.Modifiers | ForEach-Object { $_.ValueText }) -join ' '
        $type = if ($null -eq $_.Type) { '?' } else { $_.Type.ToString() }
        if ([string]::IsNullOrWhiteSpace($modifier)) { $type } else { "$modifier $type" }
    }) -join ','
}

function Get-ApiId {
    param([string] $Value)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    return 'API-' + ([Convert]::ToHexString($hash).Substring(0, 16).ToLowerInvariant())
}

$resolvedSourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
$projects = [System.Collections.Generic.List[object]]::new()
$types = [System.Collections.Generic.List[object]]::new()
$members = [System.Collections.Generic.List[object]]::new()
$diagnostics = [System.Collections.Generic.List[object]]::new()

$projectFiles = Get-ChildItem -LiteralPath (Join-Path $resolvedSourceRoot 'src') -Recurse -Filter '*.csproj' -File |
    Where-Object {
        $relative = Get-RelativePath $resolvedSourceRoot $_.FullName
        $relative.StartsWith('src/Koan.Data.', [System.StringComparison]::OrdinalIgnoreCase) -or
        $relative.StartsWith('src/Connectors/Data/', [System.StringComparison]::OrdinalIgnoreCase)
    } |
    Sort-Object FullName

foreach ($projectFile in $projectFiles) {
    $relativeProject = Get-RelativePath $resolvedSourceRoot $projectFile.FullName
    $projectRole = Get-ProjectRole $relativeProject
    $projectDir = $projectFile.DirectoryName
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name)
    $sourceFiles = Get-ChildItem -LiteralPath $projectDir -Recurse -Filter '*.cs' -File |
        Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' } |
        Sort-Object FullName

    $projects.Add([ordered]@{
        name = $projectName
        role = $projectRole
        project = $relativeProject
        sourceFiles = @($sourceFiles).Count
    })

    foreach ($sourceFile in $sourceFiles) {
        $relativeFile = Get-RelativePath $resolvedSourceRoot $sourceFile.FullName
        $text = [System.IO.File]::ReadAllText($sourceFile.FullName)
        $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($text, $null, $relativeFile)
        $root = $tree.GetRoot()
        foreach ($diagnostic in $tree.GetDiagnostics()) {
            if ($diagnostic.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Error) {
                $diagnostics.Add([ordered]@{
                    file = $relativeFile
                    id = $diagnostic.Id
                    line = $diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1
                    message = $diagnostic.GetMessage()
                })
            }
        }

        $typeNodes = $root.DescendantNodes() | Where-Object {
            $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax] -or
            $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.DelegateDeclarationSyntax]
        }
        foreach ($typeNode in $typeNodes) {
            if (-not (Test-PublicType $typeNode)) { continue }
            $fullName = Get-TypeName $typeNode
            $line = Get-LineNumber $tree $typeNode
            $typeId = Get-ApiId "$relativeFile|$line|type|$fullName"
            $types.Add([ordered]@{
                id = $typeId
                project = $projectName
                role = $projectRole
                namespace = Get-NamespaceName $typeNode
                type = $fullName
                kind = Get-TypeKind $typeNode
                line = $line
                file = $relativeFile
                partial = Has-Modifier $typeNode 'partial'
                static = Has-Modifier $typeNode 'static'
                abstract = Has-Modifier $typeNode 'abstract'
            })
        }

        $memberNodes = $root.DescendantNodes() | Where-Object {
            $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -or
            $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax] -or
            $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.DestructorDeclarationSyntax] -or
            $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax] -or
            $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.IndexerDeclarationSyntax] -or
            $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.EventDeclarationSyntax] -or
            $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.EventFieldDeclarationSyntax] -or
            $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax] -or
            $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.OperatorDeclarationSyntax] -or
            $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ConversionOperatorDeclarationSyntax] -or
            $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.EnumMemberDeclarationSyntax]
        }
        foreach ($memberNode in $memberNodes) {
            $containingType = $memberNode.Ancestors() | Where-Object {
                $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax]
            } | Select-Object -First 1
            if ($null -eq $containingType -or -not (Test-PublicType $containingType) -or -not (Test-PublicMember $memberNode)) {
                continue
            }
            $typeName = Get-TypeName $containingType
            $line = Get-LineNumber $tree $memberNode
            $kind = Get-MemberKind $memberNode
            $parameterSignature = Get-ParameterSignature $memberNode
            foreach ($name in (Get-MemberNames $memberNode)) {
                $signature = "$name($parameterSignature)"
                $memberId = Get-ApiId "$relativeFile|$line|$kind|$typeName|$signature"
                $members.Add([ordered]@{
                    id = $memberId
                    project = $projectName
                    role = $projectRole
                    type = $typeName
                    kind = $kind
                    name = $name
                    signature = $signature
                    line = $line
                    file = $relativeFile
                    static = Has-Modifier $memberNode 'static'
                    abstract = Has-Modifier $memberNode 'abstract'
                    extension = $memberNode -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and
                        $null -ne $memberNode.ParameterList -and $memberNode.ParameterList.Parameters.Count -gt 0 -and
                        (Has-Modifier $memberNode.ParameterList.Parameters[0] 'this')
                })
            }
        }
    }
}

$projectArray = @($projects | Sort-Object project)
$typeArray = @($types | Sort-Object id)
$memberArray = @($members | Sort-Object id)
$diagnosticArray = @($diagnostics | Sort-Object file, line, id)
$inventory = [ordered]@{
    schemaVersion = 1
    generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
    generator = 'New-FrameworkSurfaceAudit.ps1'
    sourceRoot = $resolvedSourceRoot.Replace('\', '/')
    sourceCommit = $SourceCommit
    scope = [ordered]@{
        included = @('src/Koan.Data.*', 'src/Connectors/Data/**')
        productionWrites = $false
        basis = 'C# syntax inventory; no restore, compilation, assembly load, or provider execution'
    }
    summary = [ordered]@{
        projects = $projectArray.Count
        publicTypes = $typeArray.Count
        publicMembers = $memberArray.Count
        parseErrors = $diagnosticArray.Count
        byRole = @($projectArray | Group-Object { $_['role'] } | Sort-Object Name | ForEach-Object {
            [ordered]@{ role = $_.Name; projects = $_.Count }
        })
    }
    projects = $projectArray
    types = $typeArray
    members = $memberArray
    diagnostics = $diagnosticArray
}

[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
$inventoryPath = Join-Path $resolvedOutput 'public-api.json'
[System.IO.File]::WriteAllText(
    $inventoryPath,
    ($inventory | ConvertTo-Json -Depth 12),
    [System.Text.UTF8Encoding]::new($false))

if (-not [string]::IsNullOrWhiteSpace($PrimerPath)) {
    $resolvedPrimer = (Resolve-Path -LiteralPath $PrimerPath).Path
    $primerText = [System.IO.File]::ReadAllText($resolvedPrimer)
    $terms = @(
        'Data.Source', 'StorageLifecycle', 'Access', 'ReadLanes', 'Inspect', 'RecordSet',
        'Query', 'Scalar', 'Lane', 'Template', 'Map', 'Container', 'Key', 'Property',
        'Name', 'Path', 'Object', 'OperationPlan', 'MappingPlan'
    )
    $vocabularyRows = foreach ($term in $terms) {
        $escaped = [Regex]::Escape($term)
        $hits = @($types | Where-Object { $_.type -match $escaped }) +
            @($members | Where-Object { $_.name -match "^$escaped$" -or $_.signature -match "^$escaped(?:``\d+)?\(" })
        [ordered]@{
            term = $term
            primerOccurrences = ([Regex]::Matches($primerText, $escaped, [Text.RegularExpressions.RegexOptions]::IgnoreCase)).Count
            publicApiOccurrences = @($hits).Count
            publicApiIds = @($hits | ForEach-Object { $_.id } | Sort-Object -Unique)
            disposition = if (@($hits).Count -gt 0) { 'PresentNameReviewRequired' } else { 'Absent' }
        }
    }
    $vocabulary = [ordered]@{
        schemaVersion = 1
        generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
        sourceCommit = $SourceCommit
        primerSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedPrimer).Hash.ToLowerInvariant()
        note = 'Exact public declaration names only; similarly named internal or unrelated members do not count.'
        terms = @($vocabularyRows)
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $resolvedOutput 'vocabulary.json'),
        ($vocabulary | ConvertTo-Json -Depth 8),
        [System.Text.UTF8Encoding]::new($false))
}

Write-Output "FRAMEWORK-SURFACE-AUDIT PASS projects=$($projectArray.Count) types=$($typeArray.Count) members=$($memberArray.Count) parseErrors=$($diagnosticArray.Count)"
