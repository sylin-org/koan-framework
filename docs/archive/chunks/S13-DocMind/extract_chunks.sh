#!/bin/bash

# S13-DocMind Document Chunking Script
# Extracts meaningful chunks from S13-DocMind-Proposal.md for AI agent processing

SOURCE_DOC="../../proposals/S13-DocMind-Proposal.md"
CHUNK_DIR="."

echo "🔄 Extracting S13-DocMind proposal chunks..."

# Validate source document exists
if [ ! -f "$SOURCE_DOC" ]; then
    echo "❌ Error: Source document not found at $SOURCE_DOC"
    exit 1
fi

# Chunk 1: Executive Overview & Problem Analysis (Lines 1-44)
echo "📋 Extracting Chunk 1: Executive Overview..."
sed -n '1,44p' "$SOURCE_DOC" > "${CHUNK_DIR}/01_executive_overview.md"

# Chunk 2: Core Entity Models (Lines 45-233)
echo "🏗️  Extracting Chunk 2: Entity Models..."
sed -n '45,233p' "$SOURCE_DOC" > "${CHUNK_DIR}/02_entity_models.md"

# Chunk 3: AI & Processing Architecture (Lines 234-442)
echo "🤖 Extracting Chunk 3: AI Processing..."
sed -n '234,442p' "$SOURCE_DOC" > "${CHUNK_DIR}/03_ai_processing.md"

# Chunk 4: API & User Interface Design (Lines 443-1165)
echo "🌐 Extracting Chunk 4: API & UI Design..."
sed -n '443,1165p' "$SOURCE_DOC" > "${CHUNK_DIR}/04_api_ui_design.md"

# Chunk 5: Infrastructure & Configuration (Lines 1166-1295)
echo "⚙️  Extracting Chunk 5: Infrastructure..."
sed -n '1166,1295p' "$SOURCE_DOC" > "${CHUNK_DIR}/05_infrastructure.md"

# Chunk 6: Implementation Strategy (Lines 1296-1833)
echo "📝 Extracting Chunk 6: Implementation Strategy..."
sed -n '1296,1833p' "$SOURCE_DOC" > "${CHUNK_DIR}/06_implementation.md"

# Chunk 7: Testing & Operations (Lines 1834-2661)
echo "🧪 Extracting Chunk 7: Testing & Operations..."
sed -n '1834,2661p' "$SOURCE_DOC" > "${CHUNK_DIR}/07_testing_ops.md"

# Chunk 8: Migration & Code Reuse Guide (Lines 2662-3274)
echo "🔄 Extracting Chunk 8: Migration Guide..."
sed -n '2662,3274p' "$SOURCE_DOC" > "${CHUNK_DIR}/08_migration_guide.md"

# Verify all chunks were created
echo ""
echo "✅ Chunk extraction complete. Files created:"
for i in {01..08}; do
    file=$(ls ${CHUNK_DIR}/${i}_*.md 2>/dev/null)
    if [ -f "$file" ]; then
        size=$(wc -l < "$file")
        echo "   📄 $file ($size lines)"
    else
        echo "   ❌ Missing chunk ${i}"
    fi
done

echo ""
echo "🎯 Next steps:"
echo "   1. Review chunk_metadata.json for AI agent assignments"
echo "   2. Use process_chunks.sh for AI agent orchestration"
echo "   3. Follow ai_agent_instructions.md for processing guidance"