#include "point.hlsl"
#include "hash-functions.hlsl"

StructuredBuffer<uint> particleGridBuffer :register(t0);
StructuredBuffer<uint2> particleGridCellBuffer :register(t1);
StructuredBuffer<uint> particleGridHashBuffer :register(t2);
StructuredBuffer<uint> particleGridCountBuffer :register(t3);
StructuredBuffer<uint> particleGridIndexBuffer :register(t4);

RWStructuredBuffer<Point> points :register(u0);


#define THREADS_PER_GROUP 256

cbuffer Params : register(b0)
{
    float3 Center;
    float ParticleGridCellSize;

    float Time;
    float CenterPointIndex;
}

static const uint            ParticleGridEntryCount = 4;
static const uint            ParticleGridCellCount = 20;
//static const float           ParticleGridCellSize = 0.1f;



bool ParticleGridFind(in float3 position, out uint startIndex, out uint endIndex)
{
    uint i;
    int3 cell = int3(position / ParticleGridCellSize);
    uint cellIndex = (pcg(cell.x + pcg(cell.y + pcg(cell.z))) % ParticleGridCellCount);
    uint hashValue = max(xxhash(cell.x + xxhash(cell.y + xxhash(cell.z))), 1);
    uint cellBegin = cellIndex * ParticleGridEntryCount;
    uint cellEnd = cellBegin + ParticleGridEntryCount;
    for(i = cellBegin; i < cellEnd; ++i)
    {
        const uint entryValue = particleGridHashBuffer[i];
        if(entryValue == hashValue)
            break;  // found existing entry
        if(entryValue == 0)
            i = cellEnd;
    }
    if(i >= cellEnd)
        return false;

    startIndex = particleGridIndexBuffer[i];
    endIndex = particleGridCountBuffer[i] + startIndex;
    return true;
}



[numthreads( THREADS_PER_GROUP, 1, 1 )]
void DispersePoints(uint3 DTid : SV_DispatchThreadID, uint GI: SV_GroupIndex)
{
    uint pointCount, stride;
    points.GetDimensions(pointCount, stride);
        
    if(DTid.x >= pointCount)
        return; // out of bounds


    float3 position = points[DTid.x].position;
    float3 jitter = (hash33u( uint3(DTid.x, DTid.x + 134775813U, DTid.x + 1664525U) + position * 1000 + Time % 123 ) -0.5f)  * ParticleGridCellSize * 2;
    //position+= jitter;

    //points[DTid.x].w = 0.1;

    if(DTid.x != (int)(CenterPointIndex) % pointCount )
        return;
 
    //int startIndex = particleGridIndexBuffer[DTid.x];
    //int endIndex = particleGridCountBuffer[DTid.x] - particke  + startIndex;

    const uint2 gridCell = particleGridCellBuffer[DTid.x];
    const uint cellIndex = gridCell.x;
    const uint gridEntryIndex =  gridCell.y;

    const uint rangeStartIndex = particleGridIndexBuffer[cellIndex];
    const uint rangeLength = particleGridCountBuffer[cellIndex];
    //const uint particleOffset =  rangeStartIndex - rangeLength + gridEntryIndex;
    const uint endIndex = rangeStartIndex + rangeLength;
    for(uint i=rangeStartIndex; i < endIndex; ++i) 
    {
        uint pointIndex = particleGridBuffer[i];
        points[pointIndex].w = 1;
    }


    // int startIndex, endIndex;
    // if(ParticleGridFind(Center, startIndex, endIndex)) 
    // {
    //     for(uint i=startIndex; i < endIndex; ++i) 
    //     {
    //         uint pointIndex = particleGridBuffer[i];
    //         points[pointIndex].w = 1;
    //     }
    // }



/*
 
    uint startIndex, endIndex;
    if(ParticleGridFind(position, startIndex, endIndex)) 
    {
        const uint particleCount = endIndex - startIndex;
        int count =0;
        float3 sumPosition = 0;

        endIndex = max(startIndex + ParticleGridEntryCount , endIndex);

        for(uint i=startIndex; i < endIndex; ++i) 
        {
            uint pointIndex = particleGridBuffer[i];
            points[pointIndex].w = 1;
            // if( pointIndex == DTid.x)
            //     continue;

            // float3 otherPos = points[pointIndex].position;
            // float3 direction = position - otherPos;
            // float distance = length(direction);
            // if(distance <= 0.0001) {
            //     distance = 0.001;
            // }

            // if(distance > Threshold)
            //     continue;
            
            // float fallOff = max(pow(((distance)/Threshold), 0.5), 0.0001);
            // direction *= fallOff;
            // float l = length(direction);
            // direction /=l;
            // direction *= min(l, ClampAccelleration);

            // sumPosition += direction;
            // count++;
        }

        // if(count > 0) 
        // {
        //     sumPosition /= count;
        //     points[DTid.x].position += sumPosition * Dispersion;            
        // }
        
    }*/
    
}