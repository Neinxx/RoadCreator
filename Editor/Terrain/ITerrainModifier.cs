namespace RoadSystem
{
    // [重构] 接口定义被简化和修正，使其更具通用性
    public interface ITerrainModifier
    {
        /// <summary>
        /// 根据 RoadManager 的数据修改地形
        /// </summary>
        /// <param name="roadManager">包含所有道路数据的管理器</param>
        void Execute(RoadManager roadManager);
    }
}