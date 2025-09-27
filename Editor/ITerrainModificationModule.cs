namespace RoadSystem
{
    // 模块化接口，让添加新功能（如植被）变得简单
    public interface ITerrainModificationModule
    {
        string ModuleName { get; }
        void Execute(TerrainModificationData data);
    }
}