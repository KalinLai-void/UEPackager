using System;
using System.Drawing;
using System.Windows.Forms;
public sealed class PlatformMoveEventArgs:EventArgs { public int Source{get;private set;} public int Target{get;private set;} public PlatformMoveEventArgs(int s,int t){Source=s;Target=t;} }
public class PlatformOrderList:DataGridView {
 public event EventHandler<PlatformMoveEventArgs> MoveRequested;
 public event ItemCheckEventHandler ItemCheck;
 public sealed class ItemCollection {
  readonly PlatformOrderList owner; public ItemCollection(PlatformOrderList o){owner=o;}
  public int Count{get{return owner.Rows.Count;}}
  public int Add(object text,bool check){return owner.Rows.Add(check,text,"\u2191","\u2193");}
  public void Clear(){owner.Rows.Clear();}
  public object this[int i]{get{return owner.Rows[i].Cells[1].Value;}set{owner.Rows[i].Cells[1].Value=value;}}
 }
 public ItemCollection Items{get;private set;}
 public int SelectedIndex {get{return CurrentCell==null?-1:CurrentCell.RowIndex;}set{if(value>=0&&value<Rows.Count)CurrentCell=Rows[value].Cells[1];else ClearSelection();}}
 int dragIndex=-1,gap=-1;Point pressed;bool adjusting;DateTime nextScroll=DateTime.MinValue;readonly string format="UEPlatform-"+Guid.NewGuid();
 public PlatformOrderList(){
  DoubleBuffered=true;AllowDrop=true;AllowUserToAddRows=false;AllowUserToDeleteRows=false;AllowUserToResizeRows=false;AllowUserToResizeColumns=false;RowHeadersVisible=false;ColumnHeadersVisible=false;MultiSelect=false;SelectionMode=DataGridViewSelectionMode.FullRowSelect;BackgroundColor=SystemColors.Window;BorderStyle=BorderStyle.FixedSingle;CellBorderStyle=DataGridViewCellBorderStyle.None;RowTemplate.Height=22;ScrollBars=ScrollBars.Vertical;AutoGenerateColumns=false;
  Columns.Add(new DataGridViewCheckBoxColumn{Width=24,SortMode=DataGridViewColumnSortMode.NotSortable});
  Columns.Add(new DataGridViewTextBoxColumn{AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});
  Columns.Add(new DataGridViewButtonColumn{Width=28,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});Columns.Add(new DataGridViewButtonColumn{Width=28,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});
  Items=new ItemCollection(this);
  CurrentCellDirtyStateChanged+=(o,e)=>{if(IsCurrentCellDirty)CommitEdit(DataGridViewDataErrorContexts.Commit);};
  CellValueChanged+=(o,e)=>{if(adjusting||e.RowIndex<0||e.ColumnIndex!=0)return;bool check=Convert.ToBoolean(Rows[e.RowIndex].Cells[0].Value);var args=new ItemCheckEventArgs(e.RowIndex,check?CheckState.Checked:CheckState.Unchecked,check?CheckState.Unchecked:CheckState.Checked);if(ItemCheck!=null)ItemCheck(this,args);if((args.NewValue==CheckState.Checked)!=check){adjusting=true;Rows[e.RowIndex].Cells[0].Value=args.NewValue==CheckState.Checked;adjusting=false;}};
  CellContentClick+=(o,e)=>{if(e.RowIndex>=0&&e.ColumnIndex>=2)RequestMove(e.RowIndex,e.RowIndex+(e.ColumnIndex==2?-1:1));};
 }
 public void BeginUpdate(){SuspendLayout();}
 public void EndUpdate(){ResumeLayout();Invalidate();}
 protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);var hit=HitTest(e.X,e.Y);dragIndex=e.Button==MouseButtons.Left&&hit.ColumnIndex==1?hit.RowIndex:-1;pressed=e.Location;}
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragIndex<0||e.Button!=MouseButtons.Left)return;var size=SystemInformation.DragSize;if(Math.Abs(e.X-pressed.X)<size.Width/2&&Math.Abs(e.Y-pressed.Y)<size.Height/2)return;var data=new DataObject();data.SetData(format,dragIndex);try{DoDragDrop(data,DragDropEffects.Move);}finally{dragIndex=-1;SetGap(-1);}}
 protected override void OnMouseUp(MouseEventArgs e){dragIndex=-1;base.OnMouseUp(e);}
 protected override void OnDragEnter(DragEventArgs e){UpdateDrop(e);base.OnDragEnter(e);}
 protected override void OnDragOver(DragEventArgs e){UpdateDrop(e);base.OnDragOver(e);}
 void UpdateDrop(DragEventArgs e){if(!Enabled||!e.Data.GetDataPresent(format)){e.Effect=DragDropEffects.None;return;}e.Effect=DragDropEffects.Move;var p=PointToClient(new Point(e.X,e.Y));if(DateTime.UtcNow>=nextScroll&&Rows.Count>0){int first=FirstDisplayedScrollingRowIndex;if(p.Y<22&&first>0){FirstDisplayedScrollingRowIndex=first-1;nextScroll=DateTime.UtcNow.AddMilliseconds(150);}else if(p.Y>ClientSize.Height-22&&first+DisplayedRowCount(false)<Rows.Count){FirstDisplayedScrollingRowIndex=first+1;nextScroll=DateTime.UtcNow.AddMilliseconds(150);}}int row=HitTest(p.X,p.Y).RowIndex;SetGap(row<0?Rows.Count:row+(p.Y>=GetRowDisplayRectangle(row,false).Top+Rows[row].Height/2?1:0));}
 void SetGap(int value){if(value==gap)return;gap=value;Invalidate();}
 protected override void OnDragLeave(EventArgs e){SetGap(-1);base.OnDragLeave(e);}
 protected override void OnDragDrop(DragEventArgs e){int source=-1,target=-1;if(Enabled&&gap>=0&&e.Data.GetDataPresent(format)){source=(int)e.Data.GetData(format);target=gap>source?gap-1:gap;}SetGap(-1);if(source>=0)RequestMove(source,target);base.OnDragDrop(e);}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(gap<0||Rows.Count==0)return;var row=GetRowDisplayRectangle(Math.Min(gap,Rows.Count-1),false);int y=gap==Rows.Count?row.Bottom-2:row.Top;using(var pen=new Pen(Color.DodgerBlue,2))e.Graphics.DrawLine(pen,0,y,ClientSize.Width,y);}
 void RequestMove(int source,int target){if(!Enabled||source<0||target<0||target>=Rows.Count||source==target)return;if(MoveRequested!=null)MoveRequested(this,new PlatformMoveEventArgs(source,target));}
}
